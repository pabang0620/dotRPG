# -*- coding: utf-8 -*-
"""Writes Assets/Resources/Data/{Quests,Cutscenes,StoryDialogues,StoryWorld}.json from the chapter modules."""
import json, os
import ch1_dialogues, ch1_story, ch2_story

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
DATA = os.path.join(ROOT, "Assets", "Resources", "Data")

def write(name, obj):
    with open(os.path.join(DATA, name), "w", encoding="utf-8") as f:
        json.dump(obj, f, ensure_ascii=False, indent=1)
        f.write("\n")

chapters = [ch1_story.CHAPTER, ch2_story.CHAPTER]
quests = list(ch1_story.QUESTS) + list(ch2_story.QUESTS)
cutscenes = list(ch1_story.CUTSCENES) + list(ch2_story.CUTSCENES)
dialogues = list(ch1_dialogues.DIALOGUES) + list(ch1_story.EXTRA_DIALOGUES) + list(ch2_story.DIALOGUES)
world = {"placements": ch1_story.WORLD["placements"] + ch2_story.WORLD["placements"], "props": ch1_story.WORLD["props"] + ch2_story.WORLD["props"]}

# Consistency checks: every referenced dialogue / cutscene exists, ids unique.
dlg_ids = {d["id"] for d in dialogues}
cut_ids = {c["id"] for c in cutscenes}
existing = {d["id"] for d in json.load(open(os.path.join(DATA, "Dialogues.json"), encoding="utf-8-sig"))["dialogues"]}
assert len(dlg_ids) == len(dialogues), "duplicate dialogue id"
assert len({q["id"] for q in quests}) == len(quests), "duplicate quest id"
problems = []
qids = {q["id"] for q in quests}
for q in quests:
    for key in ("offerDialogue", "turnInDialogue"):
        if q[key] and q[key] not in dlg_ids | existing: problems.append(f"{q['id']}.{key} -> {q[key]}")
    if q["startCutscene"] and q["startCutscene"] not in cut_ids: problems.append(f"{q['id']} start cutscene {q['startCutscene']}")
    for r in q["requires"]:
        if r not in qids: problems.append(f"{q['id']} requires unknown {r}")
    for s in q["steps"]:
        if s["cutscene"] and s["cutscene"] not in cut_ids: problems.append(f"{q['id']} step cutscene {s['cutscene']}")
        for o in s["objectives"]:
            if o["dialogue"] and o["dialogue"] not in dlg_ids | existing: problems.append(f"{q['id']} objective dialogue {o['dialogue']}")
            if o["type"] == "cutscene" and o["target"] not in cut_ids: problems.append(f"{q['id']} cutscene objective {o['target']}")
for c in cutscenes:
    for cmd in c["cmds"]:
        if cmd["op"] == "dialogue" and cmd["id"] not in dlg_ids | existing: problems.append(f"cutscene {c['id']} dialogue {cmd['id']}")
for p in world["props"]:
    if p.get("dialogue") and p["dialogue"] not in dlg_ids | existing: problems.append(f"prop {p['sprite']} dialogue {p['dialogue']}")
if problems:
    raise SystemExit("\n".join(problems))

write("Quests.json", {"chapters": chapters, "quests": quests})
write("Cutscenes.json", {"cutscenes": cutscenes})
write("StoryDialogues.json", {"dialogues": dialogues})
write("StoryWorld.json", world)
print(f"quests {len(quests)}, cutscenes {len(cutscenes)}, dialogues {len(dialogues)}, props {len(world['props'])}")
