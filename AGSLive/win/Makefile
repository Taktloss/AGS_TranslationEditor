# Builds the Windows live hook (agslive.dll) and its loader (agsinject.exe) for 32-bit AGS games,
# with the MinGW cross compiler (Fedora: mingw32-gcc)
CC = i686-w64-mingw32-gcc
CFLAGS ?= -O2 -Wall -Wextra
LDFLAGS = -static -static-libgcc -s

all: agslive.dll agsinject.exe

agslive.dll: agslive.c
	$(CC) $(CFLAGS) -shared -o $@ $< $(LDFLAGS) -lws2_32

agsinject.exe: agsinject.c
	$(CC) $(CFLAGS) -o $@ $< $(LDFLAGS)

clean:
	rm -f agslive.dll agsinject.exe
.PHONY: all clean
