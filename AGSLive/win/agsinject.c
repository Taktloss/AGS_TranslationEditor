// agsinject.exe - loads agslive.dll into an AGS game that is already running (Windows, 32-bit games).
//
//   agsinject <pid> [agslive.dll]     (default: agslive.dll next to this program)
//   agsinject --list                  running AGS games: pid <tab> hook loaded (0/1) <tab> folder name
//
// The DLL is loaded with LoadLibraryW in a thread started in the game (CreateRemoteThread). A game
// counts as AGS if its program contains the error message of the engine's get_translation. Only
// 32-bit games are handled, since this program and the DLL are 32-bit. Nothing is written to disk.
//
// Exit codes: 0 loaded, 1 usage, 2 error, 3 already loaded.
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <tlhelp32.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <wchar.h>

#define ANCHOR "!Null string supplied to CheckForTranslations"
#define LIBRARY_NAME L"agslive.dll"

static int fail(const char *message)
{
    fprintf(stderr, "agsinject: %s\n", message);
    return 2;
}

// Reads the program part of the file (headers and sections, not game data appended to the exe) and
// looks for the engine's error message; 32-bit programs only
static int is_ags_program(const wchar_t *path)
{
    HANDLE f = CreateFileW(path, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, NULL,
                           OPEN_EXISTING, 0, NULL);
    if (f == INVALID_HANDLE_VALUE)
        return 0;
    int found = 0;
    DWORD got;
    char head[4096];
    if (ReadFile(f, head, sizeof head, &got, NULL) && got == sizeof head) {
        IMAGE_DOS_HEADER *dos = (IMAGE_DOS_HEADER *)head;
        IMAGE_NT_HEADERS32 *nt = (IMAGE_NT_HEADERS32 *)(head + dos->e_lfanew);
        if (dos->e_magic == IMAGE_DOS_SIGNATURE && dos->e_lfanew > 0 && dos->e_lfanew < 2048
            && nt->Signature == IMAGE_NT_SIGNATURE && nt->FileHeader.Machine == IMAGE_FILE_MACHINE_I386) {
            // The program ends where its last section ends
            IMAGE_SECTION_HEADER *sec = IMAGE_FIRST_SECTION(nt);
            DWORD end = 0;
            for (int i = 0; i < nt->FileHeader.NumberOfSections && (char *)(sec + i + 1) <= head + sizeof head; i++)
                if (sec[i].PointerToRawData + sec[i].SizeOfRawData > end)
                    end = sec[i].PointerToRawData + sec[i].SizeOfRawData;
            char *data = end > 1024 && end < (64u << 20) ? malloc(end) : NULL;
            if (data && SetFilePointer(f, 0, NULL, FILE_BEGIN) == 0 && ReadFile(f, data, end, &got, NULL))
                for (size_t i = 0; !found && i + sizeof ANCHOR <= got; i++)
                    found = data[i] == '!' && memcmp(data + i, ANCHOR, sizeof ANCHOR) == 0;
            free(data);
        }
    }
    CloseHandle(f);
    return found;
}

static int program_path(DWORD pid, wchar_t *path, DWORD size)
{
    HANDLE p = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, FALSE, pid);
    if (!p)
        return 0;
    BOOL ok = QueryFullProcessImageNameW(p, 0, path, &size);
    CloseHandle(p);
    return ok;
}

static int hook_loaded(DWORD pid)
{
    HANDLE snap = CreateToolhelp32Snapshot(TH32CS_SNAPMODULE | TH32CS_SNAPMODULE32, pid);
    if (snap == INVALID_HANDLE_VALUE)
        return 0;
    MODULEENTRY32W me = { .dwSize = sizeof me };
    int found = 0;
    for (BOOL ok = Module32FirstW(snap, &me); ok && !found; ok = Module32NextW(snap, &me))
        found = _wcsicmp(me.szModule, LIBRARY_NAME) == 0;
    CloseHandle(snap);
    return found;
}

static int list_games(void)
{
    HANDLE snap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
    if (snap == INVALID_HANDLE_VALUE)
        return fail("cannot list the processes");
    PROCESSENTRY32W pe = { .dwSize = sizeof pe };
    DWORD me = GetCurrentProcessId();
    for (BOOL ok = Process32FirstW(snap, &pe); ok; ok = Process32NextW(snap, &pe)) {
        wchar_t path[MAX_PATH * 2];
        if (pe.th32ProcessID == me || pe.th32ProcessID == 0 || !program_path(pe.th32ProcessID, path, MAX_PATH * 2)
            || !is_ags_program(path))
            continue;
        wchar_t *slash = wcsrchr(path, L'\\');
        if (slash)
            *slash = 0;
        wchar_t *folder = wcsrchr(path, L'\\');
        char name[MAX_PATH * 3];
        WideCharToMultiByte(CP_UTF8, 0, folder ? folder + 1 : path, -1, name, sizeof name, NULL, NULL);
        printf("%lu\t%d\t%s\n", pe.th32ProcessID, hook_loaded(pe.th32ProcessID), name);
    }
    CloseHandle(snap);
    return 0;
}

int main(int argc, char **argv)
{
    if (argc == 2 && strcmp(argv[1], "--list") == 0)
        return list_games();
    DWORD pid = argc >= 2 ? strtoul(argv[1], NULL, 10) : 0;
    if (argc < 2 || argc > 3 || pid == 0) {
        fprintf(stderr, "usage: agsinject <pid> [agslive.dll]\n       agsinject --list\n"
                        "Loads the live translation hook into a running AGS game (32-bit Windows engine).\n");
        return 1;
    }

    wchar_t library[MAX_PATH * 2];
    if (argc == 3) {
        wchar_t given[MAX_PATH * 2];
        MultiByteToWideChar(CP_ACP, 0, argv[2], -1, given, MAX_PATH * 2);
        if (!GetFullPathNameW(given, MAX_PATH * 2, library, NULL))
            return fail("library not found");
    } else {
        DWORD n = GetModuleFileNameW(NULL, library, MAX_PATH * 2);
        wchar_t *slash = n ? wcsrchr(library, L'\\') : NULL;
        if (!slash || (size_t)(slash + 1 - library) + wcslen(LIBRARY_NAME) + 1 > MAX_PATH * 2)
            return fail("cannot find my own folder");
        wcscpy(slash + 1, LIBRARY_NAME);
    }
    if (GetFileAttributesW(library) == INVALID_FILE_ATTRIBUTES)
        return fail("agslive.dll not found next to agsinject");

    wchar_t path[MAX_PATH * 2];
    if (!program_path(pid, path, MAX_PATH * 2))
        return fail("no such process, or no access to it");
    if (!is_ags_program(path))
        return fail("this process is not a 32-bit AGS game (get_translation not found)");
    if (hook_loaded(pid)) {
        fprintf(stderr, "agsinject: the live hook is already loaded in process %lu\n", pid);
        return 3;
    }

    HANDLE process = OpenProcess(PROCESS_CREATE_THREAD | PROCESS_VM_OPERATION | PROCESS_VM_WRITE | PROCESS_VM_READ
                                     | PROCESS_QUERY_INFORMATION, FALSE, pid);
    if (!process)
        return fail("not allowed to open the game's process (does it run as another user or as administrator?)");
    size_t bytes = (wcslen(library) + 1) * sizeof(wchar_t);
    void *remote = VirtualAllocEx(process, NULL, bytes, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
    if (!remote || !WriteProcessMemory(process, remote, library, bytes, NULL)) {
        CloseHandle(process);
        return fail("cannot write into the game's memory");
    }
    // kernel32 is at the same address in every 32-bit process of this session
    LPTHREAD_START_ROUTINE load = (LPTHREAD_START_ROUTINE)(void *)GetProcAddress(GetModuleHandleW(L"kernel32.dll"), "LoadLibraryW");
    HANDLE thread = CreateRemoteThread(process, NULL, 0, load, remote, 0, NULL);
    if (!thread) {
        VirtualFreeEx(process, remote, 0, MEM_RELEASE);
        CloseHandle(process);
        return fail("cannot start the loading thread in the game");
    }
    DWORD result = 0;
    int finished = WaitForSingleObject(thread, 15000) == WAIT_OBJECT_0;
    if (finished)
        GetExitCodeThread(thread, &result);
    CloseHandle(thread);
    if (finished)
        VirtualFreeEx(process, remote, 0, MEM_RELEASE);
    CloseHandle(process);
    if (!finished)
        return fail("the game did not finish loading the library in time");
    if (!result)
        return fail("LoadLibrary failed in the game");
    char shown[MAX_PATH * 3];
    WideCharToMultiByte(CP_UTF8, 0, library, -1, shown, sizeof shown, NULL, NULL);
    printf("agsinject: %s loaded into process %lu\n", shown, pid);
    return 0;
}
