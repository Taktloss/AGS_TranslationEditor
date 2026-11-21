# Builds agslive.so, the live translation hook for the Linux AGS engine (x86-64), and agsinject,
# which loads it into a game that is already running
CFLAGS ?= -O2 -Wall -Wextra
all: agslive.so agsinject

agslive.so: agslive.c
	$(CC) $(CFLAGS) -shared -fPIC -o $@ $< -ldl -lpthread

agsinject: agsinject.c
	$(CC) $(CFLAGS) -o $@ $<

clean:
	rm -f agslive.so agsinject
.PHONY: all clean
