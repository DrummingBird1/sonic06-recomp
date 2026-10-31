/* TEMPORARY trace hooks (tools/re/hook.py). src/zz_* is git-ignored: never commit.
 *
 * port_trace(id, ctx) runs at the entry of each hooked function, port_trace_exit
 * after it returns. At entry ctx->gpr[3] is `this` / the first argument, ctx->lr
 * the caller; at exit ctx->gpr[3] is the return value. Guest memory is big-endian:
 * read it with vm_read8/16/32/64.
 *
 * Print to stderr and fflush: stdout is buffered and lands before stderr in the
 * run log (run.ps1 concatenates the two), so mixing them reorders lines. */
#include "ppu_memory.h"
#include <stdint.h>
#include <stdio.h>
#include <windows.h>

/* Milliseconds since the first trace line, to compare with PAD_SCRIPT times. */
static unsigned long long trace_ms(void)
{
    static unsigned long long t0;
    unsigned long long n = GetTickCount64();
    if (!t0) t0 = n;
    return n - t0;
}

/* Class name of a C++ object from its vtable: vptr[-1] is the typeinfo, whose
 * second word points at the mangled name (e.g. "N9Sonicteam8MainModeE"). */
static void trace_class_name(uint32_t obj, char* out, int size)
{
    uint32_t vt = obj ? vm_read32(obj) : 0, ti = vt ? vm_read32(vt - 4) : 0;
    uint32_t nm = ti ? vm_read32(ti + 4) : 0;
    int i = 0;
    for (; nm && i < size - 1; i++) { out[i] = (char)vm_read8(nm + i); if (!out[i]) break; }
    out[i < size ? i : size - 1] = 0;
    if (!i) snprintf(out, size, "?");
}

/* The guest call chain: the stack back-chain is 64-bit, and each frame keeps
 * its caller's return address at +16. At function entry r1 is still the
 * caller's frame. */
static void trace_chain(ppu_context* ctx)
{
    uint32_t sp = (uint32_t)ctx->gpr[1];
    fprintf(stderr, "[TRACE] chain lr=%08x", (uint32_t)ctx->lr);
    for (int i = 0; i < 14 && sp; i++) {
        uint32_t up = (uint32_t)vm_read64(sp);
        if (!up || up <= sp) break;
        fprintf(stderr, " %08x", (uint32_t)vm_read64(up + 16));
        sp = up;
    }
    fprintf(stderr, "\n");
}

void port_trace(int id, ppu_context* ctx)
{
    uint32_t self = (uint32_t)ctx->gpr[3];
    switch (id) {
    case 1: {   /* example: a state field at this+0x50, printed when it changes */
        static uint32_t last = 0xFFFFFFFFu;
        uint32_t st = vm_read32(self + 0x50);
        if (st != last) {
            char cls[64];
            trace_class_name(self, cls, sizeof cls);
            fprintf(stderr, "[TRACE] %6llums id %d %s this=%08x state %u -> %u\n",
                    trace_ms(), id, cls, self, last, st);
            fflush(stderr);
            last = st;
        }
        break;
    }
    default: {  /* count calls; print the first few with their caller chain */
        static unsigned n[64];
        unsigned c = ++n[id & 63];
        if (c <= 3 || c % 1000 == 0) {
            fprintf(stderr, "[TRACE] %6llums id %d #%u this=%08x\n", trace_ms(), id, c, self);
            if (c == 1) trace_chain(ctx);
            fflush(stderr);
        }
    }
    }
}

void port_trace_exit(int id, ppu_context* ctx)
{
    static unsigned n;
    if (++n <= 20) {
        fprintf(stderr, "[TRACE] %6llums id %d returned %08x\n",
                trace_ms(), id, (uint32_t)ctx->gpr[3]);
        fflush(stderr);
    }
}
