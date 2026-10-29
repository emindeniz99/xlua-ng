// Stress test of Assets/Plugins/iOS/HotfixFlags.cpp, the IntKey flags of iOS
// players, for Test/Native/hotfix_flags_race.py, which links the two files
// under ThreadSanitizer. Three threads read flags (ids inside and past the end
// of the array) while two threads grow the array id by id and set the even and
// the odd ids; then every flag must read back, also after half are cleared.
// Before 2026-10-29 the array grew with realloc under the readers and the two
// writers: freed memory read, lost flags or a crash.
//
//     HotfixFlagsRace [ids]
//
// Prints HOTFIX_FLAGS_RACE_PASS or exits 1.
#include <atomic>
#include <cstdio>
#include <cstdlib>
#include <thread>
#include <vector>

extern "C" int xlua_get_hotfix_flag(int idx);
extern "C" void xlua_set_hotfix_flag(int idx, int flag);

int main(int argc, char** argv) {
    const int ids = argc > 1 ? std::atoi(argv[1]) : 20000;
    std::atomic<bool> done(false);
    std::atomic<long long> reads(0);
    std::vector<std::thread> readers;
    for (unsigned r = 0; r < 3; r++) {
        readers.emplace_back([&, r] {
            unsigned x = r + 1;
            long long count = 0;
            while (!done.load(std::memory_order_relaxed)) {
                x = x * 1103515245u + 12345u;
                xlua_get_hotfix_flag((int)(x % (unsigned)(ids + 64)));
                count++;
            }
            reads += count;
        });
    }
    std::thread even([&] {
        for (int i = 0; i < ids; i += 2) xlua_set_hotfix_flag(i, 1);
    });
    std::thread odd([&] {
        for (int i = 1; i < ids; i += 2) xlua_set_hotfix_flag(i, 1);
    });
    even.join();
    odd.join();
    done = true;
    for (auto& t : readers) t.join();

    int wrong = 0;
    for (int i = 0; i < ids; i++) {
        if (xlua_get_hotfix_flag(i) != 1) wrong++;
    }
    for (int i = 0; i < ids; i += 2) xlua_set_hotfix_flag(i, 0);
    for (int i = 0; i < ids; i++) {
        if (xlua_get_hotfix_flag(i) != i % 2) wrong++;
    }
    if (xlua_get_hotfix_flag(-1) != 0 || xlua_get_hotfix_flag(ids * 4) != 0) wrong++;
    xlua_set_hotfix_flag(-1, 1); // ignored
    if (wrong) {
        std::printf("HOTFIX_FLAGS_RACE_FAIL: %d of %d flags wrong\n", wrong, ids);
        return 1;
    }
    std::printf("HOTFIX_FLAGS_RACE_PASS: %d ids set by two threads, %lld reads on three\n", ids, reads.load());
    return 0;
}
