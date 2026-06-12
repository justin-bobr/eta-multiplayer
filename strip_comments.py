import io, sys, re

files = sys.argv[1:]
for path in files:
    with io.open(path, "r", encoding="utf-8") as f:
        lines = f.readlines()
    out = []
    removed = 0
    for ln in lines:
        s = ln.lstrip()
        # drop standalone // comment lines, keep /// XML doc and everything else
        if s.startswith("//") and not s.startswith("///"):
            removed += 1
            continue
        out.append(ln)
    # collapse 3+ blank lines to a single blank
    collapsed = []
    blanks = 0
    for ln in out:
        if ln.strip() == "":
            blanks += 1
            if blanks >= 2:
                continue
        else:
            blanks = 0
        collapsed.append(ln)
    with io.open(path, "w", encoding="utf-8", newline="\n") as f:
        f.writelines(collapsed)
    print(f"{path}: removed {removed} comment lines, {len(lines)} -> {len(collapsed)} lines")
