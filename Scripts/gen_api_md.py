#!/usr/bin/env python3
import re
import sys
import os
import glob
import xml.etree.ElementTree as ET
from collections import defaultdict

GENERATED_NESTED = (".MethodName", ".PropertyName", ".SignalName")
GENERATED_METHODS = {
    "GetGodotMethodList", "GetGodotPropertyList", "GetGodotPropertyDefaultValues",
    "InvokeGodotClassMethod", "HasGodotClassMethod", "InvokeGodotClassStaticMethod",
    "HasGodotClassStaticMethod", "SetGodotClassPropertyValue", "GetGodotClassPropertyValue",
    "RaiseGodotClassSignalCallbacks",
}
TYPE_ALIASES = {
    "System.Int32": "int", "System.Int64": "long", "System.Single": "float",
    "System.Double": "double", "System.Boolean": "bool", "System.String": "string",
    "System.Byte": "byte", "System.Object": "object", "System.Void": "void",
    "System.UInt32": "uint", "System.UInt64": "ulong", "System.Single[]": "float[]",
}

USAGE = "Usage: python Scripts/gen_api_md.py <path-to-vantix.xml> <output-dir>"


def type_path_parts(full):
    short = full.rsplit(".", 1)[-1]
    ns = full.rsplit(".", 1)[0] if "." in full else ""
    return (ns.split(".") if ns else []), short


def simplify_type(t):
    t = TYPE_ALIASES.get(t, t)
    t = t.replace("Godot.Collections.", "").replace("Godot.", "")
    t = t.replace("System.Collections.Generic.", "").replace("System.", "")
    t = t.replace("{", "<").replace("}", ">")
    return t


def simplify_params(sig):
    inner = sig[1:-1] if sig.startswith("(") else sig
    if not inner:
        return "()"
    parts, depth, cur = [], 0, ""
    for ch in inner:
        if ch == "(" or ch == "{":
            depth += 1
        elif ch == ")" or ch == "}":
            depth -= 1
        if ch == "," and depth == 0:
            parts.append(cur)
            cur = ""
        else:
            cur += ch
    parts.append(cur)
    return "(" + ", ".join(simplify_type(p.strip().rstrip("@")) for p in parts) + ")"


def render(node):
    out = []
    if node.text:
        out.append(node.text)
    for child in node:
        tag = child.tag
        if tag == "see" or tag == "seealso":
            ref = child.get("cref", "") or child.get("href", "")
            label = (child.text or "").strip()
            if not label and ref:
                ref_body = ref.split(":", 1)[-1]
                if "(" in ref_body:
                    ref_body = ref_body[:ref_body.index("(")]
                label = ref_body.rsplit(".", 1)[-1]
            out.append(f"`{label}`" if label else "")
        elif tag == "paramref" or tag == "typeparamref":
            out.append(f"`{child.get('name', '')}`")
        elif tag == "c":
            out.append(f"`{(child.text or '').strip()}`")
        elif tag == "code":
            out.append(f"\n\n```\n{(child.text or '').strip()}\n```\n\n")
        elif tag == "para":
            out.append("\n\n" + render(child) + "\n\n")
        else:
            out.append(render(child))
        if child.tail:
            out.append(child.tail)
    text = "".join(out)
    text = re.sub(r"[ \t]*\n[ \t]*", " ", text)
    text = re.sub(r"\s{2,}", " ", text)
    return text.strip()


def main():
    if len(sys.argv) != 3:
        print(USAGE)
        sys.exit(1)
    xml_path, out_dir = sys.argv[1], sys.argv[2]

    os.makedirs(out_dir, exist_ok=True)
    for stale in glob.glob(os.path.join(out_dir, "**", "*.md"), recursive=True):
        os.remove(stale)
    for root, dirs, files in os.walk(out_dir, topdown=False):
        if root != out_dir and not os.listdir(root):
            os.rmdir(root)

    tree = ET.parse(xml_path)
    members = tree.find("members")

    summaries = {}
    for m in members.findall("member"):
        name = m.get("name", "")
        s = m.find("summary")
        summaries[name] = render(s) if s is not None else ""

    types = []
    for name in summaries:
        if name.startswith("T:"):
            full = name[2:]
            if any(g in full for g in GENERATED_NESTED):
                continue
            types.append(full)
    type_set = set(types)

    buckets = defaultdict(lambda: {"P": [], "M": [], "F": [], "E": []})
    for name, summary in summaries.items():
        kind = name[0]
        if kind not in ("P", "M", "F", "E"):
            continue
        body = name[2:]
        sig = ""
        if "(" in body:
            idx = body.index("(")
            sig, body = body[idx:], body[:idx]
        owner = None
        cut = body.rfind(".")
        while cut != -1:
            cand = body[:cut]
            if cand in type_set:
                owner = cand
                member = body[cut + 1:]
                break
            cut = body.rfind(".", 0, cut)
        if owner is None:
            continue
        if any(g in owner for g in GENERATED_NESTED):
            continue
        if member in GENERATED_METHODS:
            continue
        label = member + (simplify_params(sig) if kind == "M" else "")
        buckets[owner][kind].append((label, summary))

    index = defaultdict(list)
    for full in sorted(types, key=str.lower):
        short = full.rsplit(".", 1)[-1]
        ns = full.rsplit(".", 1)[0] if "." in full else "Global"
        index[ns].append((short, full))
        lines = [f"# {short}", ""]
        if "." in full:
            lines.append(f"`{full}`")
            lines.append("")
        if summaries.get("T:" + full):
            lines.append(summaries["T:" + full])
            lines.append("")
        b = buckets.get(full, {"P": [], "M": [], "F": [], "E": []})
        for kind, title in (("P", "Properties"), ("F", "Fields"), ("M", "Methods"), ("E", "Events")):
            items = sorted(b[kind])
            if not items:
                continue
            lines.append(f"## {title}")
            lines.append("")
            lines.append("| Name | Summary |")
            lines.append("|------|---------|")
            for lbl, summ in items:
                summ = summ.replace("|", "\\|").replace("\n", " ") or "—"
                lines.append(f"| `{lbl}` | {summ} |")
            lines.append("")
        parts, short_name = type_path_parts(full)
        type_dir = os.path.join(out_dir, *parts)
        os.makedirs(type_dir, exist_ok=True)
        with open(os.path.join(type_dir, short_name + ".md"), "w", encoding="utf-8") as f:
            f.write("\n".join(lines).rstrip() + "\n")

    idx = ["# VANTIX API Reference", "",
           "Auto-generated from the C# `/// <summary>` XML docs. "
           "Regenerate with `python Scripts/gen_api_md.py <vantix.xml> Docs`.", ""]
    for ns in sorted(index, key=str.lower):
        idx.append(f"## {ns}")
        idx.append("")
        for short, full in sorted(index[ns], key=lambda x: x[0].lower()):
            summ = summaries.get("T:" + full, "")
            summ = (summ[:120] + "…") if len(summ) > 120 else summ
            parts, short_name = type_path_parts(full)
            link = f"[{short}]({'/'.join(parts + [short_name])}.md)"
            idx.append(f"- {link}{(' — ' + summ) if summ else ''}")
        idx.append("")
    with open(os.path.join(out_dir, "README.md"), "w", encoding="utf-8") as f:
        f.write("\n".join(idx).rstrip() + "\n")

    print(f"Generated {len(types)} type pages + README.md in {out_dir}")


if __name__ == "__main__":
    main()
