// agslive.dll - live translation hook for Windows builds of the AGS engine (32-bit x86).
//
// The Windows engine is linked into the game's EXE without function names, so get_translation() is
// found by the one piece of code that is sure to be in it: the error message it gives for a null
// text ("!Null string supplied to CheckForTranslations"). The instruction that loads that string is
// inside the function; the function starts after the padding (int3 bytes) in front of it.
//
// The rest works like the Linux hook (AGSLive/agslive.c): every displayed text is reported to the
// translation editor, corrections sent back are shown instead. The connection is TCP to 127.0.0.1
// (port $AGSLIVE_PORT, default 47811), which also reaches a Linux editor from a game under Wine/Proton.
// Same protocol: H <pid> / T <original> <shown> to the editor, S <original> <text> / C back.
//
// Loaded by agsinject.exe into a running game, or by any other DLL loader; nothing is written to disk.
#define WIN32_LEAN_AND_MEAN
#include <winsock2.h>
#include <ws2tcpip.h>
#include <windows.h>
#include <tlhelp32.h>
#include <stdarg.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#define ANCHOR "!Null string supplied to CheckForTranslations"
#define DEFAULT_PORT 47811
#define JUMP_SIZE 5            // jmp rel32
#define RESEND_MS 2000         // texts drawn every frame are reported at most this often
#define MAX_FUNCTION_SIZE 4096 // how far before the anchor the function may start

typedef const char *(__cdecl *translate_fn)(const char *);

static translate_fn original;
static CRITICAL_SECTION lock;
static SOCKET sock = INVALID_SOCKET;
static uint8_t *hook_target;

static void log_line(const char *format, ...)
{
    char buf[512];
    va_list args;
    va_start(args, format);
    int n = vsnprintf(buf, sizeof buf - 2, format, args);
    va_end(args);
    if (n < 0)
        return;
    if (n > (int)sizeof buf - 3)
        n = sizeof buf - 3;
    buf[n++] = '\n';
    buf[n] = 0;
    OutputDebugStringA(buf);
    HANDLE err = GetStdHandle(STD_ERROR_HANDLE);
    DWORD written;
    if (err && err != INVALID_HANDLE_VALUE)
        WriteFile(err, buf, n, &written, NULL);
}

// ---------------------------------------------------------------- corrections (key -> text)

struct correction { uint64_t hash; char *key; char *text; };
static struct correction *table;
static size_t table_size, table_used;

static uint64_t hash_text(const char *s)
{
    uint64_t h = 14695981039346656037ull;
    for (; *s; s++)
        h = (h ^ (unsigned char)*s) * 1099511628211ull;
    return h;
}

static struct correction *find_slot(struct correction *t, size_t size, const char *key, uint64_t h)
{
    for (size_t i = (size_t)h & (size - 1);; i = (i + 1) & (size - 1))
        if (!t[i].key || (t[i].hash == h && strcmp(t[i].key, key) == 0))
            return &t[i];
}

// Takes ownership of key and text; text NULL removes the correction. Old texts are never freed: the
// engine may still use a pointer returned earlier. Call with the lock held.
static void set_correction(char *key, char *text)
{
    if (table_used * 2 >= table_size) {
        size_t size = table_size ? table_size * 2 : 1024;
        struct correction *t = calloc(size, sizeof *t);
        if (!t)
            return;
        for (size_t i = 0; i < table_size; i++)
            if (table[i].key)
                *find_slot(t, size, table[i].key, table[i].hash) = table[i];
        free(table);
        table = t;
        table_size = size;
    }
    uint64_t h = hash_text(key);
    struct correction *c = find_slot(table, table_size, key, h);
    if (c->key) {
        free(key);
    } else {
        c->key = key;
        c->hash = h;
        table_used++;
    }
    c->text = text;
}

static const char *get_correction(const char *key)
{
    if (!table_used)
        return NULL;
    struct correction *c = find_slot(table, table_size, key, hash_text(key));
    return c->key ? c->text : NULL;
}

// ---------------------------------------------------------------- messages

static size_t escape(const char *s, char *out)
{
    size_t n = 0;
    for (; *s; s++) {
        switch (*s) {
        case '\\': out[n++] = '\\'; out[n++] = '\\'; break;
        case '\n': out[n++] = '\\'; out[n++] = 'n'; break;
        case '\t': out[n++] = '\\'; out[n++] = 't'; break;
        case '\r': out[n++] = '\\'; out[n++] = 'r'; break;
        default: out[n++] = *s;
        }
    }
    return n;
}

static char *unescape(const char *s, size_t len)
{
    char *out = malloc(len + 1), *o = out;
    if (!out)
        return NULL;
    for (size_t i = 0; i < len; i++) {
        if (s[i] == '\\' && i + 1 < len) {
            char c = s[++i];
            *o++ = c == 'n' ? '\n' : c == 't' ? '\t' : c == 'r' ? '\r' : c;
        } else {
            *o++ = s[i];
        }
    }
    *o = 0;
    return out;
}

// Never blocks the game: the socket is non-blocking, and if the editor does not keep up, messages are dropped
static void send_message(const char *buf, size_t len)
{
    EnterCriticalSection(&lock);
    if (sock != INVALID_SOCKET && send(sock, buf, (int)len, 0) == SOCKET_ERROR && WSAGetLastError() != WSAEWOULDBLOCK)
        shutdown(sock, SD_BOTH); // the reader thread notices and reconnects
    LeaveCriticalSection(&lock);
}

static struct { uint64_t hash; ULONGLONG time; } recent[8192];

static void report(const char *text, const char *shown)
{
    uint64_t h = hash_text(text) ^ hash_text(shown);
    size_t slot = (size_t)h & (sizeof recent / sizeof recent[0] - 1);
    ULONGLONG t = GetTickCount64();
    if (recent[slot].hash == h && t - recent[slot].time < RESEND_MS)
        return;
    recent[slot].hash = h;
    recent[slot].time = t;

    char *buf = malloc(2 * (strlen(text) + strlen(shown)) + 8);
    if (!buf)
        return;
    size_t n = 0;
    buf[n++] = 'T';
    buf[n++] = '\t';
    n += escape(text, buf + n);
    buf[n++] = '\t';
    n += escape(shown, buf + n);
    buf[n++] = '\n';
    send_message(buf, n);
    free(buf);
}

// ---------------------------------------------------------------- the hook

static const char *__cdecl hooked_get_translation(const char *text)
{
    // The original also sets the speech display time, so it always runs
    const char *shown = original(text);
    if (!text || !*text)
        return shown;

    EnterCriticalSection(&lock);
    const char *corrected = get_correction(text);
    LeaveCriticalSection(&lock);
    if (corrected && *corrected)
        shown = corrected;

    if (sock != INVALID_SOCKET)
        report(text, shown);
    return shown;
}

// ---------------------------------------------------------------- connection to the editor

static void handle_line(char *line, size_t len)
{
    if (len == 1 && line[0] == 'C') {
        EnterCriticalSection(&lock);
        for (size_t i = 0; i < table_size; i++)
            table[i].text = NULL;
        LeaveCriticalSection(&lock);
        return;
    }
    if (len < 3 || line[0] != 'S' || line[1] != '\t')
        return;
    char *key = line + 2, *tab = memchr(key, '\t', len - 2);
    if (!tab)
        return;
    char *k = unescape(key, tab - key);
    char *t = unescape(tab + 1, line + len - tab - 1);
    if (t && !*t) {
        free(t);
        t = NULL;
    }
    EnterCriticalSection(&lock);
    if (k)
        set_correction(k, t);
    LeaveCriticalSection(&lock);
}

static int port(void)
{
    char value[16];
    DWORD n = GetEnvironmentVariableA("AGSLIVE_PORT", value, sizeof value);
    int p = n > 0 && n < sizeof value ? atoi(value) : 0;
    return p > 0 && p < 65536 ? p : DEFAULT_PORT;
}

static DWORD WINAPI connection_thread(LPVOID arg)
{
    (void)arg;
    WSADATA wsa;
    if (WSAStartup(MAKEWORD(2, 2), &wsa) != 0) {
        log_line("agslive: no network (WSAStartup failed)");
        return 0;
    }
    struct sockaddr_in addr = { 0 };
    addr.sin_family = AF_INET;
    addr.sin_port = htons((u_short)port());
    addr.sin_addr.s_addr = htonl(INADDR_LOOPBACK);
    log_line("agslive: waiting for the editor at 127.0.0.1:%d", ntohs(addr.sin_port));
    for (;;) {
        SOCKET s = socket(AF_INET, SOCK_STREAM, IPPROTO_TCP);
        if (s == INVALID_SOCKET || connect(s, (struct sockaddr *)&addr, sizeof addr) != 0) {
            if (s != INVALID_SOCKET)
                closesocket(s);
            Sleep(1000);
            continue;
        }
        BOOL nodelay = TRUE;
        setsockopt(s, IPPROTO_TCP, TCP_NODELAY, (const char *)&nodelay, sizeof nodelay);
        char hello[32];
        int n = snprintf(hello, sizeof hello, "H\t%lu\n", GetCurrentProcessId());
        send(s, hello, n, 0);
        u_long nonblocking = 1;
        ioctlsocket(s, FIONBIO, &nonblocking);
        memset(recent, 0, sizeof recent); // report everything to a new editor
        EnterCriticalSection(&lock);
        sock = s;
        LeaveCriticalSection(&lock);
        log_line("agslive: editor connected");

        size_t cap = 65536, used = 0;
        char *buf = malloc(cap);
        for (;;) {
            fd_set readable;
            FD_ZERO(&readable);
            FD_SET(s, &readable);
            struct timeval wait = { 1, 0 };
            int ready = select(0, &readable, NULL, NULL, &wait);
            if (ready == SOCKET_ERROR)
                break;
            if (ready == 0)
                continue;
            int got = buf ? recv(s, buf + used, (int)(cap - used), 0) : 0;
            if (got == SOCKET_ERROR && WSAGetLastError() == WSAEWOULDBLOCK)
                continue;
            if (got <= 0)
                break;
            used += got;
            char *start = buf, *nl;
            while ((nl = memchr(start, '\n', buf + used - start))) {
                handle_line(start, nl - start);
                start = nl + 1;
            }
            used = buf + used - start;
            memmove(buf, start, used);
            if (used == cap) {
                char *bigger = realloc(buf, cap * 2);
                if (!bigger)
                    break;
                buf = bigger;
                cap *= 2;
            }
        }
        free(buf);
        EnterCriticalSection(&lock);
        sock = INVALID_SOCKET;
        closesocket(s);
        LeaveCriticalSection(&lock);
        log_line("agslive: editor disconnected");
    }
}

// ---------------------------------------------------------------- finding get_translation

struct section { uint8_t *start; size_t size; };

static int find_section(uint8_t *base, const char *name, struct section *out)
{
    IMAGE_DOS_HEADER *dos = (IMAGE_DOS_HEADER *)base;
    IMAGE_NT_HEADERS *nt = (IMAGE_NT_HEADERS *)(base + dos->e_lfanew);
    IMAGE_SECTION_HEADER *s = IMAGE_FIRST_SECTION(nt);
    for (int i = 0; i < nt->FileHeader.NumberOfSections; i++, s++)
        if (strncmp((const char *)s->Name, name, 8) == 0) {
            out->start = base + s->VirtualAddress;
            out->size = s->Misc.VirtualSize;
            return 1;
        }
    return 0;
}

static uint8_t *find_bytes(uint8_t *from, size_t size, const void *what, size_t len)
{
    for (size_t i = 0; i + len <= size; i++)
        if (from[i] == *(const uint8_t *)what && memcmp(from + i, what, len) == 0)
            return from + i;
    return NULL;
}

// Length of the instructions that the jump overwrites. Only simple prologue instructions without
// relative operands are accepted, because they are moved to the trampoline unchanged.
static size_t prologue_length(const uint8_t *p, size_t need)
{
    size_t n = 0;
    while (n < need) {
        const uint8_t *i = p + n;
        if (i[0] >= 0x50 && i[0] <= 0x57)
            n += 1;                                     // push reg
        else if ((i[0] == 0x8B || i[0] == 0x89 || i[0] == 0x33 || i[0] == 0x85) && (i[1] & 0xC0) == 0xC0)
            n += 2;                                     // mov/xor/test reg, reg (mov ebp, esp)
        else if (i[0] == 0x8B && (i[1] & 0xC7) == 0x45)
            n += 3;                                     // mov reg, [ebp+disp8]
        else if (i[0] == 0x83 && i[1] == 0xEC)
            n += 3;                                     // sub esp, imm8
        else if (i[0] == 0x81 && i[1] == 0xEC)
            n += 6;                                     // sub esp, imm32
        else if (i[0] == 0x6A)
            n += 2;                                     // push imm8
        else if (i[0] == 0x68)
            n += 5;                                     // push imm32
        else
            return 0;
    }
    return n;
}

static uint8_t *find_get_translation(void)
{
    uint8_t *base = (uint8_t *)GetModuleHandleW(NULL);
    struct section text, rdata;
    if (!find_section(base, ".text", &text) || !find_section(base, ".rdata", &rdata)) {
        log_line("agslive: the game's program has no .text/.rdata section");
        return NULL;
    }
    uint8_t *anchor = find_bytes(rdata.start, rdata.size, ANCHOR, sizeof ANCHOR); // with its terminating 0
    if (!anchor) {
        log_line("agslive: this is not an AGS engine (the get_translation error message was not found)");
        return NULL;
    }
    // The 32-bit engine loads the address as an immediate: mov reg, imm32 (B8-BF) or push imm32 (68)
    uint32_t address = (uint32_t)(uintptr_t)anchor;
    for (uint8_t *ref = text.start; (ref = find_bytes(ref, text.start + text.size - ref, &address, 4)); ref++) {
        uint8_t op = ref[-1];
        if (!((op >= 0xB8 && op <= 0xBF) || op == 0x68))
            continue;
        // The function starts after the int3 padding before it; compilers align functions to 16 bytes
        for (uint8_t *p = ref - 1; p > ref - MAX_FUNCTION_SIZE && p > text.start; p--)
            if (((uintptr_t)p & 15) == 0 && p[-1] == 0xCC && p[0] != 0xCC && prologue_length(p, JUMP_SIZE))
                return p;
    }
    log_line("agslive: the code using the get_translation error message was not found");
    return NULL;
}

// ---------------------------------------------------------------- installing the hook

// Other threads are stopped while the 5 bytes are written, so none runs into a half-written jump
static void suspend_others(int resume)
{
    HANDLE snap = CreateToolhelp32Snapshot(TH32CS_SNAPTHREAD, 0);
    if (snap == INVALID_HANDLE_VALUE)
        return;
    THREADENTRY32 te = { .dwSize = sizeof te };
    DWORD me = GetCurrentThreadId(), pid = GetCurrentProcessId();
    for (BOOL ok = Thread32First(snap, &te); ok; ok = Thread32Next(snap, &te)) {
        if (te.th32OwnerProcessID != pid || te.th32ThreadID == me)
            continue;
        HANDLE t = OpenThread(THREAD_SUSPEND_RESUME | THREAD_GET_CONTEXT, FALSE, te.th32ThreadID);
        if (!t)
            continue;
        if (resume) {
            ResumeThread(t);
        } else {
            SuspendThread(t);
            // A thread stopped inside the bytes that are about to change runs on a little first
            for (int tries = 0; tries < 50; tries++) {
                CONTEXT ctx = { .ContextFlags = CONTEXT_CONTROL };
                if (!GetThreadContext(t, &ctx) || (uint8_t *)(uintptr_t)ctx.Eip < hook_target
                    || (uint8_t *)(uintptr_t)ctx.Eip >= hook_target + JUMP_SIZE)
                    break;
                ResumeThread(t);
                Sleep(1);
                SuspendThread(t);
            }
        }
        CloseHandle(t);
    }
    CloseHandle(snap);
}

static void write_jump(uint8_t *at, const void *to)
{
    int32_t rel = (int32_t)((const uint8_t *)to - (at + JUMP_SIZE));
    at[0] = 0xE9;
    memcpy(at + 1, &rel, 4);
}

static DWORD WINAPI install(LPVOID arg)
{
    (void)arg;
    uint8_t *target = find_get_translation();
    if (!target)
        return 0;
    size_t len = prologue_length(target, JUMP_SIZE);
    if (!len) {
        log_line("agslive: unknown instructions at the start of get_translation: %02x %02x %02x %02x %02x %02x %02x %02x",
                 target[0], target[1], target[2], target[3], target[4], target[5], target[6], target[7]);
        return 0;
    }
    uint8_t *trampoline = VirtualAlloc(NULL, 64, MEM_COMMIT | MEM_RESERVE, PAGE_EXECUTE_READWRITE);
    if (!trampoline) {
        log_line("agslive: no memory for the trampoline");
        return 0;
    }
    memcpy(trampoline, target, len);
    write_jump(trampoline + len, target + len);
    original = (translate_fn)(void *)trampoline;

    DWORD old;
    if (!VirtualProtect(target, JUMP_SIZE, PAGE_EXECUTE_READWRITE, &old)) {
        log_line("agslive: cannot change the game's code");
        return 0;
    }
    hook_target = target;
    suspend_others(0);
    write_jump(target, (void *)hooked_get_translation);
    FlushInstructionCache(GetCurrentProcess(), target, JUMP_SIZE);
    suspend_others(1);
    VirtualProtect(target, JUMP_SIZE, old, &old);
    log_line("agslive: get_translation hooked at %p", (void *)target);

    connection_thread(NULL);
    return 0;
}

BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, LPVOID reserved)
{
    (void)reserved;
    if (reason == DLL_PROCESS_ATTACH) {
        DisableThreadLibraryCalls(instance);
        InitializeCriticalSection(&lock);
        // Not while the loader lock is held: the work happens in a thread of its own
        HANDLE t = CreateThread(NULL, 0, install, NULL, 0, NULL);
        if (t)
            CloseHandle(t);
    }
    return TRUE;
}
