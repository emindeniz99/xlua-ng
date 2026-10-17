/* Entry point for the static OpenHarmony x86_64 smoke binary that
 * Test/Native/ohos_check.sh links (-Wl,-e,xlua_ohos_x86_64_entry).
 *
 * The x86_64 libc.a in the OpenHarmony native SDK (checked with 26.0.0.38)
 * compiles __init_libc with the stack protector. Its prologue reads the canary
 * at %fs:0x28 before __init_tls has set the thread pointer, and Linux starts a
 * process with FS base 0, so every static executable, even hello world, stops
 * with SIGSEGV at address 0x28 before main. Upstream musl builds this code
 * without the stack protector, and so does the SDK's aarch64 libc.a, which is
 * why arm64-v8a runs. A dynamically linked program is not affected: the
 * loader sets the thread pointer first.
 *
 * This entry point points FS at a scratch thread block whose canary is the
 * value __init_ssp will store (the first 8 bytes of AT_RANDOM with byte 1
 * cleared), so the canary __init_libc saves and the one it checks on return
 * agree. Then it jumps to the libc _start with the stack unchanged, and
 * __init_tls replaces the scratch block with the real one. If a later SDK
 * derives the canary differently, __init_libc fails its check and the smoke
 * test stops in __stack_chk_fail instead of passing silently.
 */
#if !defined(__x86_64__)
#error "ohos_x86_64_entry.c is for x86_64 only"
#endif
#include <stdint.h>

#define AUX_AT_RANDOM 25
#define ARCH_SET_FS 0x1002
#define SYS_ARCH_PRCTL 158
#define TCB_CANARY_SLOT (0x28 / sizeof(uintptr_t)) /* musl struct pthread: self, dtv, prev, next, sysinfo, canary */

static uintptr_t scratch_tcb[16] __attribute__((aligned(64)));

__attribute__((visibility("hidden"), no_stack_protector, used))
void xlua_ohos_x86_64_set_scratch_tls(uintptr_t *sp);

void xlua_ohos_x86_64_set_scratch_tls(uintptr_t *sp) {
    uintptr_t *p = sp + 1 + sp[0] + 1; /* skip argc, argv[] and its NULL */
    uintptr_t canary = 0;
    long ret;
    while (*p) p++; /* skip envp[] */
    for (p++; p[0] != 0; p += 2) {
        if (p[0] == AUX_AT_RANDOM) {
            const unsigned char *random = (const unsigned char *)p[1];
            for (unsigned i = 0; i < sizeof canary; i++) canary |= (uintptr_t)random[i] << (8 * i);
            break;
        }
    }
    canary &= ~(uintptr_t)0xff00;
    scratch_tcb[0] = (uintptr_t)scratch_tcb; /* self pointer, read by __pthread_self */
    scratch_tcb[TCB_CANARY_SLOT] = canary;
    __asm__ volatile("syscall"
                     : "=a"(ret)
                     : "a"((long)SYS_ARCH_PRCTL), "D"((long)ARCH_SET_FS), "S"(scratch_tcb)
                     : "rcx", "r11", "memory");
    if (ret != 0) __builtin_trap();
}

/* At entry %rsp points at argc and is 16-byte aligned, so the call leaves the
 * callee with the alignment the ABI expects, and _start sees the original
 * stack. */
__asm__(".text\n"
        ".globl xlua_ohos_x86_64_entry\n"
        ".type xlua_ohos_x86_64_entry,@function\n"
        "xlua_ohos_x86_64_entry:\n"
        "    mov %rsp, %rdi\n"
        "    call xlua_ohos_x86_64_set_scratch_tls\n"
        "    jmp _start\n"
        ".size xlua_ohos_x86_64_entry, .-xlua_ohos_x86_64_entry\n");
