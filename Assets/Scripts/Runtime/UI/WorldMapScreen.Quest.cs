using UnityEngine;
using UnityEngine.UI;

namespace DotRPG
{
    /// <summary>
    /// [UI] Quest destination on the world map: a pulsing "▼" on the region map (the goal NPC or spot, or the exit toward
    /// the goal's map on the current map) and over the goal's region on the connection chart, plus one line naming it.
    /// </summary>
    public partial class WorldMapScreen
    {
        Text questPin, atlasQuestPin, questLine;
        Image questLinePlate;
        bool questPinLive; // the pin follows a live position on the current map (Update moves it)

        static readonly Color32 QuestGold = new Color32(255, 214, 64, 255);

        /// <summary>One line over the map: what the followed quest wants and where.</summary>
        void RefreshQuestLine(QuestDestination.Info dest)
        {
            if (questLinePlate == null)
            {
                // On the frame (not the map viewport) so it stays above the connection chart too: bottom-left of the map area.
                var frame = mapRect.parent.parent;
                questLinePlate = UIFactory.Image(frame, "QuestGoalPlate", Game.Art.Get("ui_white"), new Color32(14, 20, 26, 215));
                questLinePlate.preserveAspect = false;
                questLinePlate.raycastTarget = false;
                UIFactory.Place(questLinePlate.rectTransform, TL, TL, new Vector2(14f, -440f), new Vector2(560f, 26f));
                questLine = UIFactory.Text(questLinePlate.transform, "QuestGoal", "", 16, QuestGold, TextAnchor.MiddleLeft, true);
                UIFactory.Stretch(questLine.rectTransform, 8f, 0f, 8f, 0f);
                questLine.raycastTarget = false;
            }
            questLinePlate.gameObject.SetActive(dest != null);
            if (dest == null) return;
            string where = L.T(MapRegistry.Get(dest.map)?.displayName ?? dest.map);
            questLine.text = L.F("▼ 퀘스트 목적지: {0}", where) + (string.IsNullOrEmpty(dest.label) ? "" : "  <color=#e8e2d0>(" + dest.label + ")</color>");
            questLinePlate.rectTransform.sizeDelta = new Vector2(Mathf.Clamp(questLine.preferredWidth + 18f, 200f, 720f), 26f);
            questLinePlate.transform.SetAsLastSibling();
        }

        /// <summary>Called at the end of <see cref="Refresh"/>: places the pin on the region map shown now.</summary>
        void RefreshQuestPin()
        {
            var dest = QuestDestination.Current;
            RefreshQuestLine(dest);
            if (questPin == null || questPin.transform.parent != mapRect) questPin = QuestDestination.CreateMark(mapRect, "QuestPin", 26);
            questPinLive = false;
            bool show = false;
            Vector2 at = Vector2.zero;
            if (dest != null && preview != null)
            {
                if (selectedMap == Game.World.MapId)
                {
                    show = QuestDestination.PointHere(dest, out var point, out _);
                    at = point;
                    questPinLive = show;
                }
                else if (dest.map == selectedMap)
                {
                    show = true;
                    at = preview.bounds.center; // a hunting ground or a place: the whole region
                    foreach (var m in npcs)
                        if ((dest.kind == "Npc" && m.id == dest.id) || (dest.kind == "DungeonGuide" && m.service == NpcService.Dungeon)) { at = m.position; break; }
                }
            }
            questPin.gameObject.SetActive(show);
            if (!show) return;
            questPin.rectTransform.anchoredPosition = Place(at) + new Vector2(0f, 22f);
            questPin.transform.SetAsLastSibling();
        }

        /// <summary>Called at the end of <see cref="RefreshAtlas"/>: marks the goal's region on the chart.</summary>
        void RefreshAtlasQuestPin()
        {
            var dest = QuestDestination.Current;
            RefreshQuestLine(dest);
            if (atlasContents == null) return;
            if (atlasQuestPin == null || atlasQuestPin.transform.parent != atlasContents) atlasQuestPin = QuestDestination.CreateMark(atlasContents, "AtlasQuestPin", 28);
            string region = QuestDestination.RegionOf(dest);
            bool show = false;
            foreach (var entry in atlasDots)
            {
                if (entry.id != region) continue;
                var rt = atlasQuestPin.rectTransform;
                rt.anchorMin = rt.anchorMax = entry.dot.rectTransform.anchorMin;
                rt.anchoredPosition = entry.dot.rectTransform.anchoredPosition + new Vector2(0f, 30f);
                show = true;
                break;
            }
            atlasQuestPin.gameObject.SetActive(show);
            if (show) atlasQuestPin.transform.SetAsLastSibling();
        }

        /// <summary>Pulses the pins and keeps the live pin on its moving target.</summary>
        void UpdateQuestPins()
        {
            if (questPin != null && questPin.gameObject.activeSelf)
            {
                QuestDestination.Pulse(questPin.transform);
                if (questPinLive && preview != null && QuestDestination.PointHere(QuestDestination.Current, out var point, out _))
                    questPin.rectTransform.anchoredPosition = Place(point) + new Vector2(0f, 22f);
            }
            if (atlasQuestPin != null && atlasQuestPin.gameObject.activeSelf) QuestDestination.Pulse(atlasQuestPin.transform);
        }
    }
}
