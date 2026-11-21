// agsinject - loads agslive.so into an AGS game that is already running (Linux, x86-64).
//
//   agsinject <pid> [library.so]       (default: agslive.so next to this program)
//   agsinject --list                   running AGS games of this user: pid, hooked (0/1), folder name
//
// The game's main thread is stopped with ptrace while it waits in a system call (so it holds no
// malloc or loader lock), made to call dlopen(library) and then put back exactly as it was. The
// library's constructor installs the hook as with LD_PRELOAD. Only processes of the same user whose
// program exports the AGS engine's get_translation are accepted. Nothing is written to disk.
//
// Exit codes: 0 loaded, 1 usage, 2 error, 3 already loaded.
#define _GNU_SOURCE
#include <dirent.h>
#include <elf.h>
#include <errno.h>
#include <fcntl.h>
#include <limits.h>
#include <signal.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/ptrace.h>
#include <sys/stat.h>
#include <sys/user.h>
#include <sys/wait.h>
#include <time.h>
#include <unistd.h>

#define TARGET_SYMBOL "_Z15get_translationPKc"
#define RTLD_NOW_FLAG 2
#define RTLD_DLOPEN_FLAG 0x80000000 // __RTLD_DLOPEN, for __libc_dlopen_mode
#define SYS_FUTEX_NR 202
#define ATTEMPTS 200

static pid_t pid;

static int fail(const char *message)
{
    fprintf(stderr, "agsinject: %s\n", message);
    return 2;
}

// ---------------------------------------------------------------- ELF: find a dynamic symbol in a file

// Returns the symbol's address relative to the file's load address, or 0.
static uint64_t elf_symbol(const char *path, const char *name, uint64_t *first_vaddr)
{
    int fd = open(path, O_RDONLY | O_CLOEXEC);
    if (fd < 0)
        return 0;
    uint64_t result = 0;
    struct stat st;
    if (fstat(fd, &st) != 0 || st.st_size < (off_t)sizeof(Elf64_Ehdr) || st.st_size > (off_t)1 << 30)
        goto done;
    uint8_t *file = malloc(st.st_size);
    if (!file || pread(fd, file, st.st_size, 0) != st.st_size) {
        free(file);
        goto done;
    }
    Elf64_Ehdr *eh = (Elf64_Ehdr *)file;
    if (memcmp(eh->e_ident, ELFMAG, SELFMAG) != 0 || eh->e_ident[EI_CLASS] != ELFCLASS64 || eh->e_machine != EM_X86_64
        || eh->e_shoff == 0 || eh->e_shoff + (uint64_t)eh->e_shnum * sizeof(Elf64_Shdr) > (uint64_t)st.st_size
        || eh->e_phoff + (uint64_t)eh->e_phnum * sizeof(Elf64_Phdr) > (uint64_t)st.st_size)
        goto free_file;
    if (first_vaddr) {
        *first_vaddr = 0;
        Elf64_Phdr *ph = (Elf64_Phdr *)(file + eh->e_phoff);
        for (int i = 0; i < eh->e_phnum; i++)
            if (ph[i].p_type == PT_LOAD) {
                *first_vaddr = ph[i].p_vaddr & ~(uint64_t)0xFFF;
                break;
            }
    }
    Elf64_Shdr *sh = (Elf64_Shdr *)(file + eh->e_shoff);
    for (int i = 0; i < eh->e_shnum && !result; i++) {
        if (sh[i].sh_type != SHT_DYNSYM || sh[i].sh_link >= eh->e_shnum)
            continue;
        Elf64_Shdr *strtab = &sh[sh[i].sh_link];
        if (sh[i].sh_offset + sh[i].sh_size > (uint64_t)st.st_size || strtab->sh_offset + strtab->sh_size > (uint64_t)st.st_size)
            continue;
        Elf64_Sym *syms = (Elf64_Sym *)(file + sh[i].sh_offset);
        size_t count = sh[i].sh_size / sizeof(Elf64_Sym);
        const char *strs = (const char *)(file + strtab->sh_offset);
        for (size_t s = 0; s < count; s++) {
            if (syms[s].st_name >= strtab->sh_size || syms[s].st_value == 0 || syms[s].st_shndx == SHN_UNDEF)
                continue;
            if (ELF64_ST_TYPE(syms[s].st_info) == STT_FUNC && strcmp(strs + syms[s].st_name, name) == 0) {
                result = syms[s].st_value;
                break;
            }
        }
    }
free_file:
    free(file);
done:
    close(fd);
    return result;
}

// Load address of the C library in the game and the path to read it (through /proc/<pid>/root, so
// games in a container such as the Steam runtime work too).
static int find_libc(uint64_t *base, char *file, size_t size)
{
    char maps[64];
    snprintf(maps, sizeof maps, "/proc/%d/maps", pid);
    FILE *f = fopen(maps, "r");
    if (!f)
        return 0;
    char line[PATH_MAX + 128];
    int found = 0;
    while (fgets(line, sizeof line, f)) {
        unsigned long start, offset;
        char path[PATH_MAX] = "";
        if (sscanf(line, "%lx-%*x %*s %lx %*s %*s %4095[^\n]", &start, &offset, path) < 3 || offset != 0)
            continue;
        const char *name = strrchr(path, '/');
        name = name ? name + 1 : path;
        if (strcmp(name, "libc.so.6") == 0 || (strncmp(name, "libc-", 5) == 0 && strstr(name, ".so"))) {
            *base = start;
            snprintf(file, size, "/proc/%d/root%s", pid, path);
            found = 1;
            break;
        }
    }
    fclose(f);
    return found;
}

static int already_loaded(const char *library)
{
    char maps[64];
    snprintf(maps, sizeof maps, "/proc/%d/maps", pid);
    FILE *f = fopen(maps, "r");
    if (!f)
        return 0;
    const char *name = strrchr(library, '/');
    name = name ? name + 1 : library;
    char line[PATH_MAX + 128];
    int found = 0;
    while (!found && fgets(line, sizeof line, f)) {
        char *end = line + strcspn(line, "\n");
        *end = 0;
        size_t n = strlen(name);
        found = (size_t)(end - line) >= n + 1 && strcmp(end - n, name) == 0 && end[-(long)n - 1] == '/';
    }
    fclose(f);
    return found;
}

// Prints the running AGS games of this user, one per line: pid <tab> hook loaded (0/1) <tab> name of
// the engine's folder (usually the game folder).
static int list_games(void)
{
    DIR *dir = opendir("/proc");
    if (!dir)
        return fail("cannot read /proc");
    struct dirent *e;
    while ((e = readdir(dir))) {
        char *end;
        long id = strtol(e->d_name, &end, 10);
        if (*end || id <= 1 || id == getpid())
            continue;
        char path[64], exe[PATH_MAX];
        snprintf(path, sizeof path, "/proc/%ld", id);
        struct stat st;
        if (stat(path, &st) != 0 || st.st_uid != getuid())
            continue;
        snprintf(path, sizeof path, "/proc/%ld/exe", id);
        ssize_t n = readlink(path, exe, sizeof exe - 1);
        // Engines are a few MB; skip big programs (browsers etc.) instead of reading them whole
        if (n <= 0 || stat(path, &st) != 0 || st.st_size > (off_t)64 << 20)
            continue;
        exe[n] = 0;
        if (!elf_symbol(path, TARGET_SYMBOL, NULL))
            continue;
        pid = (pid_t)id;
        char *slash = strrchr(exe, '/');
        if (slash)
            *slash = 0;
        const char *folder = strrchr(exe, '/');
        printf("%ld\t%d\t%s\n", id, already_loaded("agslive.so"), folder ? folder + 1 : exe);
    }
    closedir(dir);
    return 0;
}

// ---------------------------------------------------------------- ptrace

static int wait_stop(int *status)
{
    for (;;) {
        if (waitpid(pid, status, __WALL) < 0) {
            if (errno == EINTR)
                continue;
            return 0;
        }
        return WIFSTOPPED(*status);
    }
}

// The system call the stopped thread is in, or -1 if it is running user code.
static long current_syscall(void)
{
    char path[64], text[256] = "";
    snprintf(path, sizeof path, "/proc/%d/syscall", pid);
    FILE *f = fopen(path, "r");
    if (!f)
        return -1;
    if (!fgets(text, sizeof text, f))
        text[0] = 0;
    fclose(f);
    if (text[0] < '0' || text[0] > '9')
        return -1; // "running" or "-1 ..." (not in a system call)
    return strtol(text, NULL, 10);
}

static int write_memory(uint64_t address, const void *data, size_t length)
{
    const uint8_t *bytes = data;
    for (size_t i = 0; i < length; i += sizeof(long)) {
        long word = 0;
        size_t n = length - i < sizeof(long) ? length - i : sizeof(long);
        if (n < sizeof(long)) {
            errno = 0;
            word = ptrace(PTRACE_PEEKDATA, pid, (void *)(address + i), NULL);
            if (errno)
                return 0;
        }
        memcpy(&word, bytes + i, n);
        if (ptrace(PTRACE_POKEDATA, pid, (void *)(address + i), (void *)word) != 0)
            return 0;
    }
    return 1;
}

// Stops the main thread at a moment it waits in a system call other than futex.
static int stop_in_syscall(void)
{
    if (ptrace(PTRACE_SEIZE, pid, NULL, NULL) != 0) {
        if (errno == EPERM)
            fprintf(stderr, "agsinject: not allowed to attach to process %d. If /proc/sys/kernel/yama/ptrace_scope is 1 or "
                            "more, allow it for this session with: sudo sysctl kernel.yama.ptrace_scope=0\n", pid);
        else
            fprintf(stderr, "agsinject: cannot attach to process %d: %s\n", pid, strerror(errno));
        return 0;
    }
    for (int attempt = 0; attempt < ATTEMPTS; attempt++) {
        int status;
        if (ptrace(PTRACE_INTERRUPT, pid, NULL, NULL) != 0 || !wait_stop(&status))
            return 0;
        long call = current_syscall();
        if (call >= 0 && call != SYS_FUTEX_NR)
            return 1;
        // Busy in game code (or waiting for a lock): let it run a moment and look again
        if (ptrace(PTRACE_CONT, pid, NULL, NULL) != 0)
            return 0;
        struct timespec pause = { 0, 5 * 1000 * 1000 };
        nanosleep(&pause, NULL);
    }
    fprintf(stderr, "agsinject: the game never waited in a system call; try again (e.g. while it shows a still scene)\n");
    ptrace(PTRACE_INTERRUPT, pid, NULL, NULL);
    int status;
    wait_stop(&status);
    ptrace(PTRACE_DETACH, pid, NULL, NULL);
    return 0;
}

int main(int argc, char **argv)
{
    if (argc == 2 && strcmp(argv[1], "--list") == 0)
        return list_games();
    if (argc < 2 || argc > 3 || !(pid = (pid_t)strtol(argv[1], NULL, 10)) || pid <= 1) {
        fprintf(stderr, "usage: agsinject <pid> [library.so]\n       agsinject --list\n"
                        "Loads the live translation hook into a running Linux AGS game.\n");
        return 1;
    }

    char library[PATH_MAX];
    if (argc == 3) {
        if (!realpath(argv[2], library))
            return fail("library not found");
    } else {
        char self[PATH_MAX];
        ssize_t n = readlink("/proc/self/exe", self, sizeof self - 1);
        if (n <= 0)
            return fail("cannot find my own folder");
        self[n] = 0;
        char *slash = strrchr(self, '/');
        *slash = 0;
        if (snprintf(library, sizeof library, "%s/agslive.so", self) >= (int)sizeof library || access(library, R_OK) != 0)
            return fail("agslive.so not found next to agsinject");
    }

    // Only AGS engines of this user
    char path[PATH_MAX];
    snprintf(path, sizeof path, "/proc/%d", pid);
    struct stat st;
    if (stat(path, &st) != 0)
        return fail("no such process");
    if (st.st_uid != getuid() && getuid() != 0)
        return fail("the process belongs to another user");
    snprintf(path, sizeof path, "/proc/%d/exe", pid);
    if (!elf_symbol(path, TARGET_SYMBOL, NULL))
        return fail("this process is not a Linux AGS engine with exported symbols (get_translation not found)");
    if (already_loaded(library)) {
        fprintf(stderr, "agsinject: the live hook is already loaded in process %d\n", pid);
        return 3;
    }

    // dlopen in the game's C library (glibc 2.34+), or __libc_dlopen_mode in older ones
    uint64_t libc_base, libc_vaddr = 0;
    char libc_file[PATH_MAX + 32];
    if (!find_libc(&libc_base, libc_file, sizeof libc_file))
        return fail("the game's C library was not found in its memory map");
    uint64_t flags = RTLD_NOW_FLAG;
    uint64_t offset = elf_symbol(libc_file, "dlopen", &libc_vaddr);
    if (!offset) {
        offset = elf_symbol(libc_file, "__libc_dlopen_mode", &libc_vaddr);
        flags |= RTLD_DLOPEN_FLAG;
    }
    if (!offset)
        return fail("dlopen not found in the game's C library");
    uint64_t dlopen_address = libc_base - libc_vaddr + offset;

    if (!stop_in_syscall())
        return 2;

    struct user_regs_struct saved, regs;
    if (ptrace(PTRACE_GETREGS, pid, NULL, &saved) != 0) {
        ptrace(PTRACE_DETACH, pid, NULL, NULL);
        return fail("cannot read the registers");
    }

    // Call dlopen(library, flags) on the game's stack, below the red zone, returning to address 0
    regs = saved;
    size_t length = strlen(library) + 1;
    uint64_t text = ((saved.rsp - 256 - length) & ~(uint64_t)15);
    uint64_t stack = text - 64;
    uint64_t zero = 0;
    stack -= 8; // return address; at the call, rsp + 8 is 16-byte aligned
    if (!write_memory(text, library, length) || !write_memory(stack, &zero, sizeof zero)) {
        ptrace(PTRACE_DETACH, pid, NULL, NULL);
        return fail("cannot write to the game's stack");
    }
    regs.rsp = stack;
    regs.rip = dlopen_address;
    regs.rdi = text;
    regs.rsi = flags;
    regs.rax = 0;
    regs.orig_rax = (uint64_t)-1; // no system call restart for our call
    if (ptrace(PTRACE_SETREGS, pid, NULL, &regs) != 0 || ptrace(PTRACE_CONT, pid, NULL, NULL) != 0) {
        ptrace(PTRACE_SETREGS, pid, NULL, &saved);
        ptrace(PTRACE_DETACH, pid, NULL, NULL);
        return fail("cannot start the call");
    }

    // Wait until dlopen returns to address 0; pass on other signals meanwhile
    int status;
    for (;;) {
        if (!wait_stop(&status))
            return fail("the game ended during the injection");
        int sig = WSTOPSIG(status);
        if (sig == SIGSEGV) {
            if (ptrace(PTRACE_GETREGS, pid, NULL, &regs) != 0)
                return fail("cannot read the result");
            if (regs.rip == 0)
                break;
            // A real crash inside dlopen: put the thread back and let it continue
            ptrace(PTRACE_SETREGS, pid, NULL, &saved);
            ptrace(PTRACE_DETACH, pid, NULL, NULL);
            return fail("the game crashed while loading the library; the thread was restored");
        }
        int pass = (status >> 16) == PTRACE_EVENT_STOP ? 0 : sig;
        if (ptrace(PTRACE_CONT, pid, NULL, (void *)(long)pass) != 0)
            return fail("lost the game during the injection");
    }

    uint64_t handle = regs.rax;
    ptrace(PTRACE_SETREGS, pid, NULL, &saved);
    ptrace(PTRACE_DETACH, pid, NULL, NULL);
    if (!handle)
        return fail("dlopen failed in the game (its error output may say why; is the library path visible inside the game's container?)");
    printf("agsinject: %s loaded into process %d\n", library, pid);
    return 0;
}
