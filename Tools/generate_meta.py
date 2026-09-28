#!/usr/bin/env python3
"""Creates missing Unity .meta files (deterministic GUIDs) and the hand-authored data assets/scene.

Only needed because this project was bootstrapped without the Unity editor. Unity keeps existing
.meta files; running this again never changes a GUID that already exists.
"""
import hashlib, os, sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ASSETS = os.path.join(ROOT, "Assets")

def guid_for(rel):
    return hashlib.md5(("dotRPG:" + rel.replace(os.sep, "/")).encode()).hexdigest()

def meta_body(path, guid):
    if os.path.isdir(path):
        return f"fileFormatVersion: 2\nguid: {guid}\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {{}}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n"
    ext = os.path.splitext(path)[1].lower()
    if ext == ".cs":
        return (f"fileFormatVersion: 2\nguid: {guid}\nMonoImporter:\n  externalObjects: {{}}\n  serializedVersion: 2\n"
                "  defaultReferences: []\n  executionOrder: 0\n  icon: {instanceID: 0}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n")
    if ext == ".asmdef":
        return f"fileFormatVersion: 2\nguid: {guid}\nAssemblyDefinitionImporter:\n  externalObjects: {{}}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n"
    if ext in (".json", ".txt", ".md", ".bytes", ".csv"):
        return f"fileFormatVersion: 2\nguid: {guid}\nTextScriptImporter:\n  externalObjects: {{}}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n"
    if ext == ".asset":
        return f"fileFormatVersion: 2\nguid: {guid}\nNativeFormatImporter:\n  externalObjects: {{}}\n  mainObjectFileID: 11400000\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n"
    return f"fileFormatVersion: 2\nguid: {guid}\nDefaultImporter:\n  externalObjects: {{}}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n"

def read_guid(meta):
    for line in open(meta, encoding="utf-8"):
        if line.startswith("guid:"):
            return line.split(":", 1)[1].strip()
    return None

def ensure_meta(path):
    meta = path + ".meta"
    rel = os.path.relpath(path, ROOT)
    if os.path.exists(meta):
        return read_guid(meta)
    guid = guid_for(rel)
    with open(meta, "w", encoding="utf-8", newline="\n") as f:
        f.write(meta_body(path, guid))
    return guid

def main():
    count = 0
    for dirpath, dirnames, filenames in os.walk(ASSETS):
        dirnames[:] = [d for d in dirnames if not d.startswith(".")]
        for name in dirnames + filenames:
            if name.endswith(".meta") or name.startswith("."):
                continue
            p = os.path.join(dirpath, name)
            if not os.path.exists(p + ".meta"):
                ensure_meta(p)
                count += 1
    print(f"created {count} meta files")

if __name__ == "__main__":
    main()
