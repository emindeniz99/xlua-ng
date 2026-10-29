// The IntKey hotfix flags of iOS players. An IntKey method that uses the
// process-wide id list (xLua's own assembly without generated HotfixBridges;
// docs/research/hotfix.md, "The iOS IntKey flag") calls
// HotfixDelegateBridge.xlua_get_hotfix_flag(id) at its entry, and
// HotfixDelegateBridge.Set writes the flag when a patch is installed or removed.
//
// Readers run on any thread, also while a patch is installed. Until 2026-10-29
// the array grew with realloc while another thread could read it, and a reader
// could then read freed memory. Now writers take a lock, a grown array is
// published with a release store, and an array is never freed: each one keeps a
// pointer to the one it replaced, so a reader that still holds an old array
// reads valid memory. The array doubles, so the old ones take less memory than
// the one in use. Test/Native/hotfix_flags_race.py checks this under
// ThreadSanitizer.
#include <atomic>
#include <climits>
#include <mutex>
#include <new>

namespace {

struct HotfixFlagArray {
    int length;
    std::atomic<int>* flags;
    HotfixFlagArray* previous;
};

std::atomic<HotfixFlagArray*> hotfix_flags(nullptr);
std::mutex hotfix_flags_write;

}

extern "C" {

int xlua_get_hotfix_flag(int idx) {
    HotfixFlagArray* array = hotfix_flags.load(std::memory_order_acquire);
    if (array == nullptr || idx < 0 || idx >= array->length) {
        return 0;
    }
    return array->flags[idx].load(std::memory_order_acquire);
}

void xlua_set_hotfix_flag(int idx, int flag) {
    if (idx < 0 || idx == INT_MAX) {
        return;
    }
    std::lock_guard<std::mutex> guard(hotfix_flags_write);
    HotfixFlagArray* array = hotfix_flags.load(std::memory_order_relaxed);
    if (array == nullptr || idx >= array->length) {
        long long old_length = array == nullptr ? 0 : array->length;
        long long length = old_length * 2 > idx ? old_length * 2 : (long long)idx + 1;
        if (length > INT_MAX) {
            length = INT_MAX;
        }
        HotfixFlagArray* grown = new (std::nothrow) HotfixFlagArray;
        std::atomic<int>* flags = grown == nullptr ? nullptr : new (std::nothrow) std::atomic<int>[length];
        if (flags == nullptr) { // out of memory: skip, as before
            delete grown;
            return;
        }
        for (long long i = 0; i < length; i++) {
            flags[i].store(i < old_length ? array->flags[i].load(std::memory_order_relaxed) : 0, std::memory_order_relaxed);
        }
        grown->length = (int)length;
        grown->flags = flags;
        grown->previous = array;
        hotfix_flags.store(grown, std::memory_order_release);
        array = grown;
    }
    array->flags[idx].store(flag, std::memory_order_release);
}

}
