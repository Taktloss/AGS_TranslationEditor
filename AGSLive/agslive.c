// agslive.so - live translation hook for the Linux AGS engine, loaded with LD_PRELOAD.
//
// Every text the game shows goes through the engine function get_translation(). This library
// redirects that function, sends each displayed text to the translation editor over a Unix
// socket and returns corrections the editor sends back, while the game runs.
//
// Protocol (one message per line, fields separated by tabs; \ newline tab and CR escaped as
// \\ \n \t \r; texts are raw bytes in the game's text encoding):
//   game -> editor   H <pid>                      hello after connecting
//                    T <original> <shown text>    a text was displayed
//   editor -> game   S <original> <correction>    show this instead (empty = use the TRA again)
//                    C                            drop all corrections
#define _GNU_SOURCE
#include <dlfcn.h>
#include <errno.h>
#include <pthread.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/mman.h>
#include <sys/socket.h>
#include <sys/un.h>
#include <time.h>
#include <unistd.h>

#define TARGET_SYMBOL "_Z15get_translationPKc" // const char *get_translation(const char *)
#define JUMP_SIZE 14                            // jmp qword ptr [rip+0]; dq target
#define RESEND_SECONDS 2.0                      // texts drawn every frame are reported at most this often

typedef const char *(*translate_fn)(const char *);

static translate_fn original;
static pthread_mutex_t lock = PTHREAD_MUTEX_INITIALIZER;
static int sock = -1;

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
    for (size_t i = h & (size - 1);; i = (i + 1) & (size - 1))
        if (!t[i].key || (t[i].hash == h && strcmp(t[i].key, key) == 0))
            return &t[i];
}

// Takes ownership of key and text; text NULL removes the correction. Old texts are never freed:
// the engine may still use a pointer returned earlier. Call with lock held.
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

// Never blocks the game: if the editor does not keep up, messages are dropped
static void send_message(const char *buf, size_t len)
{
    pthread_mutex_lock(&lock);
    if (sock >= 0 && send(sock, buf, len, MSG_DONTWAIT | MSG_NOSIGNAL) < 0
        && errno != EAGAIN && errno != EWOULDBLOCK) {
        shutdown(sock, SHUT_RDWR); // the reader thread notices and reconnects
    }
    pthread_mutex_unlock(&lock);
}

static struct { uint64_t hash; double time; } recent[8192];

static double now(void)
{
    struct timespec ts;
    clock_gettime(CLOCK_MONOTONIC, &ts);
    return ts.tv_sec + ts.tv_nsec / 1e9;
}

static void report(const char *text, const char *shown)
{
    uint64_t h = hash_text(text) ^ hash_text(shown);
    size_t slot = h & (sizeof recent / sizeof recent[0] - 1);
    double t = now();
    if (recent[slot].hash == h && t - recent[slot].time < RESEND_SECONDS)
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

static const char *hooked_get_translation(const char *text)
{
    // The original also sets the speech display time, so it always runs
    const char *shown = original(text);
    if (!text || !*text)
        return shown;

    pthread_mutex_lock(&lock);
    const char *corrected = get_correction(text);
    pthread_mutex_unlock(&lock);
    if (corrected && *corrected)
        shown = corrected;

    if (sock >= 0)
        report(text, shown);
    return shown;
}

// ---------------------------------------------------------------- connection to the editor

static void socket_path(struct sockaddr_un *addr)
{
    const char *path = getenv("AGSLIVE_SOCKET");
    const char *dir = getenv("XDG_RUNTIME_DIR");
    memset(addr, 0, sizeof *addr);
    addr->sun_family = AF_UNIX;
    if (path && *path) {
        if (strlen(path) >= sizeof addr->sun_path)
            fprintf(stderr, "agslive: AGSLIVE_SOCKET is longer than %zu characters and gets cut off\n",
                    sizeof addr->sun_path - 1);
        snprintf(addr->sun_path, sizeof addr->sun_path, "%s", path);
    }
    else if (dir && *dir)
        snprintf(addr->sun_path, sizeof addr->sun_path, "%s/agslive.sock", dir);
    else
        snprintf(addr->sun_path, sizeof addr->sun_path, "/tmp/agslive-%d.sock", (int)getuid());
}

static void handle_line(char *line, size_t len)
{
    if (len == 1 && line[0] == 'C') {
        pthread_mutex_lock(&lock);
        for (size_t i = 0; i < table_size; i++)
            table[i].text = NULL;
        pthread_mutex_unlock(&lock);
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
    pthread_mutex_lock(&lock);
    if (k)
        set_correction(k, t);
    pthread_mutex_unlock(&lock);
}

static void *connection_thread(void *arg)
{
    (void)arg;
    struct sockaddr_un addr;
    socket_path(&addr);
    fprintf(stderr, "agslive: waiting for the editor at %s\n", addr.sun_path);
    for (;;) {
        int s = socket(AF_UNIX, SOCK_STREAM | SOCK_CLOEXEC, 0);
        if (s < 0 || connect(s, (struct sockaddr *)&addr, sizeof addr) < 0) {
            if (s >= 0)
                close(s);
            sleep(1);
            continue;
        }
        char hello[32];
        int n = snprintf(hello, sizeof hello, "H\t%d\n", (int)getpid());
        send(s, hello, n, MSG_NOSIGNAL);
        memset(recent, 0, sizeof recent); // report everything to a new editor
        pthread_mutex_lock(&lock);
        sock = s;
        pthread_mutex_unlock(&lock);
        fprintf(stderr, "agslive: editor connected\n");

        size_t cap = 65536, used = 0;
        char *buf = malloc(cap);
        ssize_t got;
        while (buf && (got = recv(s, buf + used, cap - used, 0)) > 0) {
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
        pthread_mutex_lock(&lock);
        sock = -1;
        close(s);
        pthread_mutex_unlock(&lock);
        fprintf(stderr, "agslive: editor disconnected\n");
    }
    return NULL;
}

// ---------------------------------------------------------------- installing the hook

// Length of the instructions that the jump overwrites. Only simple prologue instructions without
// RIP-relative operands are accepted, because they are moved to the trampoline unchanged.
static size_t prologue_length(const uint8_t *p, size_t need)
{
    size_t n = 0;
    while (n < need) {
        const uint8_t *i = p + n;
        int rexw = (i[0] & 0xF8) == 0x48;                     // REX.W prefix 48-4F
        if (i[0] == 0xF3 && i[1] == 0x0F && i[2] == 0x1E && i[3] == 0xFA)
            n += 4;                                           // endbr64
        else if (i[0] >= 0x50 && i[0] <= 0x57)
            n += 1;                                           // push reg
        else if (i[0] == 0x41 && i[1] >= 0x50 && i[1] <= 0x57)
            n += 2;                                           // push r8-r15
        else if ((i[0] == 0x31 || i[0] == 0x33 || i[0] == 0x85 || i[0] == 0x89 || i[0] == 0x8B)
                 && (i[1] & 0xC0) == 0xC0)
            n += 2;                                           // xor/test/mov reg, reg
        else if (rexw && (i[1] == 0x31 || i[1] == 0x33 || i[1] == 0x85 || i[1] == 0x89 || i[1] == 0x8B)
                 && (i[2] & 0xC0) == 0xC0)
            n += 3;                                           // 64-bit xor/test/mov reg, reg
        else if (rexw && i[1] == 0x83 && (i[2] & 0xC0) == 0xC0)
            n += 4;                                           // add/sub reg, imm8
        else if (rexw && i[1] == 0x81 && (i[2] & 0xC0) == 0xC0)
            n += 7;                                           // add/sub reg, imm32
        else if (i[0] == 0x64 && (i[1] & 0xF8) == 0x48 && i[2] == 0x8B && (i[3] & 0xC7) == 0x04
                 && i[4] == 0x25)
            n += 9;                                           // mov reg, fs:[imm32] (stack guard)
        else
            return 0;
    }
    return n;
}

static void write_jump(uint8_t *at, const void *to)
{
    static const uint8_t jmp[6] = { 0xFF, 0x25, 0, 0, 0, 0 };
    memcpy(at, jmp, sizeof jmp);
    memcpy(at + sizeof jmp, &to, sizeof to);
}

__attribute__((constructor)) static void agslive_init(void)
{
    uint8_t *target = dlsym(RTLD_DEFAULT, TARGET_SYMBOL);
    if (!target) {
        fprintf(stderr, "agslive: %s not found - not a Linux AGS 3.x engine with exported symbols\n", TARGET_SYMBOL);
        return;
    }
    size_t len = prologue_length(target, JUMP_SIZE);
    if (!len) {
        fprintf(stderr, "agslive: unknown instructions at the start of get_translation:");
        for (int i = 0; i < 16; i++)
            fprintf(stderr, " %02x", target[i]);
        fprintf(stderr, "\n");
        return;
    }

    uint8_t *trampoline = mmap(NULL, 64, PROT_READ | PROT_WRITE | PROT_EXEC, MAP_PRIVATE | MAP_ANONYMOUS, -1, 0);
    if (trampoline == MAP_FAILED) {
        perror("agslive: mmap");
        return;
    }
    memcpy(trampoline, target, len);
    write_jump(trampoline + len, target + len);
    original = (translate_fn)trampoline;

    long page = sysconf(_SC_PAGESIZE);
    uint8_t *first = (uint8_t *)((uintptr_t)target & ~(uintptr_t)(page - 1));
    size_t span = (size_t)(target + JUMP_SIZE - first);
    if (mprotect(first, span, PROT_READ | PROT_WRITE | PROT_EXEC) != 0) {
        perror("agslive: mprotect");
        return;
    }
    write_jump(target, (void *)hooked_get_translation);
    mprotect(first, span, PROT_READ | PROT_EXEC);

    pthread_t thread;
    if (pthread_create(&thread, NULL, connection_thread, NULL) == 0)
        pthread_detach(thread);
    fprintf(stderr, "agslive: get_translation hooked\n");
}
