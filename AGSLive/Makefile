# Builds agslive.so, the live translation hook for the Linux AGS engine (x86-64)
CFLAGS ?= -O2 -Wall -Wextra
agslive.so: agslive.c
	$(CC) $(CFLAGS) -shared -fPIC -o $@ $< -ldl -lpthread

clean:
	rm -f agslive.so
.PHONY: clean
