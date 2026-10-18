import subprocess, sys
scan = sys.argv[1]
root = "/Volumes/T7Data/ext-ai-projects/xlua-ng/ultra/size-core/projects/android-"
for p in ["core-5.5", "ng-5.5-minimal", "baseline"]:
    for d in ["PlayerScriptAssemblies", "artifacts/Android/ManagedStripped"]:
        print("==", p, d)
        out = subprocess.run(["mono", scan, root + p + "/Library/Bee/" + d, "--only", "XLua.Core,XLua"],
                             stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True).stdout
        for line in out.splitlines():
            if line.startswith("ASSEMBLY XLua") or "AUTO_PROP" in line and "XLua" in line or "GVM_" in line or line.startswith("TOTAL"):
                print(line)
