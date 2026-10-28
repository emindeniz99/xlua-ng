#!/usr/bin/env python3
"""Checks of build/xlua_chunkc.c, the chunk compiler of bytecode-only builds.

    python3 Test/Native/chunkc_check.py BUILD/xlua_chunkc WORK_DIR

Runs the program on the C# sources of the runtime (with and without the
options, stripped and not) and on small C# files that each break one rule of
the <xlua-chunk> markers, and checks its exit status, its message and the
header it writes. With build/CMakeLists.txt's XLUA_COVERAGE the program is
instrumented, and Test/Native/coverage.sh measures it with these runs
(Test/README.md, "Static analysis, coverage and fuzzing"). Out of memory is
checked on Linux only, under an address space limit (RLIMIT_AS), which macOS
does not enforce. Prints XLUA_CHUNKC_CHECK_PASS when every check passes.
"""
import os
from pathlib import Path
import shutil
import subprocess
import sys

repo = Path(__file__).resolve().parents[2]


def main(argv):
    if len(argv) != 3:
        sys.exit(__doc__)
    chunkc = str(Path(argv[1]).resolve())
    work = Path(argv[2]).resolve()
    shutil.rmtree(work, ignore_errors=True)
    work.mkdir(parents=True)
    count = 0

    def run(args, status, message=None, limit=None):
        nonlocal count
        preexec = None
        if limit is not None:
            import resource

            def preexec():
                resource.setrlimit(resource.RLIMIT_AS, (limit, limit))
        result = subprocess.run([chunkc] + [str(a) for a in args], cwd=work, capture_output=True, text=True,
                                timeout=60, preexec_fn=preexec)
        assert result.returncode == status, (args, result.returncode, result.stderr)
        if message is not None:
            assert message in result.stderr, (args, message, result.stderr)
        count += 1
        return result

    def source(name, text):
        path = work / name
        with open(path, "w", encoding="utf-8", newline="") as f:
            f.write(text)
        return path

    def marker(attributes, literal, tail=";\n// </xlua-chunk>\n"):
        return "// <xlua-chunk %s>\nconst string F = %s%s" % (attributes, literal, tail)

    out = work / "out.h"
    cs = sorted((repo / "Assets" / "XLua" / "Src").glob("*.cs"))
    options = ["-D", "XLUA_BYTECODE_UTIL", "-D", "XLUA_BYTECODE_PROFILER", "-D", "XLUA_BYTECODE_MEMORY"]

    # The runtime's chunks: every option (as the build), none (the optional modules
    # left out), stripped; an option -D that no chunk asks for changes nothing.
    run(options + ["-o", out] + cs, 0)
    full = out.read_text()
    for name in ["remove_libraries", "xlua.util", "perf.profiler", "xlua.memory"]:
        assert '"%s"' % name in full, name
    assert not (work / "out.h.tmp").exists()
    run(["-D", "OTHER", "-o", out] + cs, 0)
    assert '"xlua.util"' not in out.read_text() and '"remove_libraries"' in out.read_text()
    run(["-s"] + options + ["-o", out] + cs, 0)
    assert len(out.read_text()) < len(full)

    # Arguments.
    run([], 1, "usage: xlua_chunkc")
    run(["-o"], 1, "unknown option -o")  # -o and -D without a value
    run(["-o", out, "-D"], 1, "unknown option -D")
    run(["-x", "-o", out], 1, "unknown option -x")
    run(["-o", out], 1, "no <xlua-chunk> markers found")
    run(["-o", out, source("none.cs", "class C {}\n")], 1, "no <xlua-chunk> markers found")
    run(["-o", out, source("empty.cs", "")], 1, "no <xlua-chunk> markers found")
    run(["-o", out, work / "missing.cs"], 1, "cannot open " + str(work / "missing.cs"))
    (work / "dir.cs").mkdir()
    run(["-o", out, work / "dir.cs"], 1, "cannot read")  # fopen opens a directory, fread fails
    run(["-o", out, source("nul.cs", "a\0b")], 1, "a NUL byte in")

    # A chunk with every accepted form: the attribute keys inside other words,
    # "" for a double quote, CR LF lines and tabs (the hash skips CR), a backslash
    # in the name (escaped in the header), an empty string, and a marker on the
    # file's last line.
    good = source("good.cs", '// <xlua-chunk xname="no" names="no" name=x name="a\\b" chunkname="=good">\r\n'
                  'const string A = @"return ""q""\r\n\t" \t\r\n;\r\n\t// </xlua-chunk>\r\n'
                  '/* <xlua-chunk name="empty" chunkname="=empty"> */ const string B = @""; // </xlua-chunk>')
    run(["-o", out, good], 0)
    header = out.read_text()
    assert '"a\\\\b"' in header and '"empty"' in header, header

    # Each broken rule.
    bad = [
        ('// <xlua-chunk name="a" chunkname="b"\n>', "an unterminated <xlua-chunk> marker"),
        ('// <xlua-chunk name="a" chunkname="b"', "an unterminated <xlua-chunk> marker"),
        (marker('name="" chunkname="b"', '@"x"'), "an empty or unterminated attribute name"),
        (marker('chunkname="b" name="a', '@"x"'), "an empty or unterminated attribute name"),
        (marker('chunkname="b"', '@"x"'), "a marker without name or chunkname"),
        (marker('name="a"', '@"x"'), "a marker without name or chunkname"),
        ('// <xlua-chunk name="a" chunkname="b">\nconst string F = "x";\n// </xlua-chunk>\n', "no verbatim string"),
        ('// <xlua-chunk name="a" chunkname="b">\nconst string F = @"x";\n', "no verbatim string"),
        ('// <xlua-chunk name="a" chunkname="b">\n// </xlua-chunk>\nconst string F = @"x";\n', "no verbatim string"),
        (marker('name="a" chunkname="b"', '$@"x"'), "an interpolated string for a"),
        ('// <xlua-chunk name="a" chunkname="b">\nconst string F = @"x\n// </xlua-chunk>\n',
         "an unterminated string for a"),
        (marker('name="a" chunkname="b"', '@"\x01"'), "a character that is not printable ASCII in a"),
        (marker('name="a" chunkname="b"', '@"\x7f"'), "a character that is not printable ASCII in a"),
        (marker('name="a" chunkname="b"', '@"x" + @"y"'), "more than one string literal for a"),
        (marker('name="a" chunkname="b"', '@"x"', "; x\n// </xlua-chunk>\n"), "no </xlua-chunk> right after"),
        (marker('name="a" chunkname="b"', '@"x"', ";\n// x </xlua-chunk>\n"), "no </xlua-chunk> right after"),
        (marker('name="a" chunkname="=b"', '@"return +"'), "xlua_chunkc: a (" + str(work / "bad.cs") + "): b:1:"),
    ]
    for text, message in bad:
        run(["-o", out, source("bad.cs", text)], 1, message)
    run(["-o", out, source("bad.cs", marker('name="a" chunkname="b"', '@"é"'))], 1,
        "a character that is not printable ASCII in a")
    # The same name twice, in one file or in two.
    one = marker('name="a" chunkname="b"', '@"x = 1"')
    run(["-o", out, source("bad.cs", one + one)], 1, "chunk a in %s and in %s" % (work / "bad.cs", work / "bad.cs"))
    run(["-o", out, source("bad.cs", one), source("bad2.cs", one)], 1,
        "chunk a in %s and in %s" % (work / "bad.cs", work / "bad2.cs"))

    # The header cannot be written, flushed or renamed.
    run(["-o", work / "missing" / "out.h", good], 1, "cannot write " + str(work / "missing" / "out.h.tmp"))
    taken = work / "taken.h"
    taken.mkdir()
    (taken / "keep").write_text("")  # remove() leaves a directory that is not empty
    run(["-o", taken, good], 1, "cannot rename the header to " + str(taken))
    assert (taken / "keep").exists()
    if sys.platform.startswith("linux") and os.path.exists("/dev/full"):
        (work / "full.h.tmp").symlink_to("/dev/full")
        run(["-o", work / "full.h", good], 1, "cannot write " + str(work / "full.h.tmp"))
        # Out of memory: /dev/zero has no end, so the read grows until the limit refuses it.
        run(["-o", out, "/dev/zero"], 1, "xlua_chunkc: out of memory", limit=256 << 20)
    else:
        print("chunkc_check: /dev/full and RLIMIT_AS are Linux only; skipped those two checks")
    print("XLUA_CHUNKC_CHECK_PASS: %d runs" % count)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
