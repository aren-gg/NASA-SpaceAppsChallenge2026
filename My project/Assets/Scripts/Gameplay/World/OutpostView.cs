using MarsTrainer.Core;
using MarsTrainer.Gameplay.Farm;
using MarsTrainer.Sim;
using UnityEngine;

namespace MarsTrainer.Gameplay.World
{
    // All equipment displays the shared mission. No second farm, battery or clock.
    public sealed class OutpostView : MonoBehaviour
    {
        private MissionSession mission;
        private Transform[] crops;
        private TextMesh[] captions;
        private bool subscribed;
        private bool captionsDirty;

        public void Initialize(TerrainGenerator world, MissionSession source, Material structure, Material leaves, Material panel)
        {
            mission = source;
            var equipment = new GameObject("Outpost equipment").transform;
            equipment.SetParent(transform, false);
            equipment.localPosition = Vector3.up * (world.SampleHeight(0, 0) + GameConstants.BaseDeckThickness);
            var farm = equipment.gameObject.AddComponent<FarmInteract>();
            farm.Bind(source.Simulation, source.Farm);
            crops = new Transform[source.Farm.Plots.Count];
            captions = new TextMesh[crops.Length];
            for (int i = 0; i < crops.Length; i++)
            {
                var bed = TerrainGenerator.Box(equipment, "Greenhouse plot " + (i + 1),
                    new Vector3((i - (crops.Length - 1) * .5f) * GameConstants.OutpostPlotSpacing,
                        GameConstants.BaseDeckThickness / 2, GameConstants.OutpostEquipmentOffset),
                    new Vector3(GameConstants.OutpostPlotWidth, GameConstants.BaseDeckThickness, GameConstants.OutpostPlotDepth), structure);
                var plot = bed.AddComponent<GreenhousePlot>(); plot.Initialize(farm, i);
                // Keep the plant independent of the bed's nonuniform scale.
                crops[i] = TerrainGenerator.Box(equipment, "Crop canopy " + (i + 1), bed.transform.localPosition,
                    Vector3.one, leaves, false).transform;
                if (!world.OpenWorldActive) captions[i] = Label(equipment, bed.transform.localPosition + Vector3.up * GameConstants.OutpostLabelHeight, "Plant with Tend farm");
            }
            for (int i = 0; i < GameConstants.MissionPanels; i++)
            {
                var solar = TerrainGenerator.Box(equipment, "Solar panel " + (i + 1),
                    new Vector3((i % GameConstants.OutpostPanelColumns - (GameConstants.OutpostPanelColumns - 1) * .5f) * GameConstants.OutpostPanelSpacing,
                        GameConstants.OutpostPanelHeight, -GameConstants.OutpostEquipmentOffset + i / GameConstants.OutpostPanelColumns * GameConstants.OutpostPanelSpacing),
                    new Vector3(GameConstants.OutpostPanelWidth, GameConstants.BaseDeckThickness / 2, GameConstants.OutpostPanelDepth), panel, false);
                solar.transform.localRotation = Quaternion.Euler(GameConstants.OutpostPanelTilt, 0, 0);
            }
            if (!world.OpenWorldActive) Label(equipment, new Vector3(0, GameConstants.OutpostLabelHeight, -GameConstants.OutpostEquipmentOffset), "SOLAR ARRAY");
            TerrainGenerator.Box(equipment, "Life support and medbay", new Vector3(-GameConstants.OutpostEquipmentOffset,
                GameConstants.PlayerHeight / 2, 0), new Vector3(GameConstants.OutpostPlotDepth, GameConstants.PlayerHeight, GameConstants.OutpostPlotWidth), structure);
            if (!world.OpenWorldActive) Label(equipment, new Vector3(-GameConstants.OutpostEquipmentOffset, GameConstants.OutpostLabelHeight, 0), "HABITAT / MEDBAY");
            Subscribe();
            for (int i = 0; i < crops.Length; i++) Refresh(i);
            RefreshCaptions();
        }

        private static TextMesh Label(Transform parent, Vector3 position, string caption)
        {
            var owner = new GameObject(caption); owner.transform.SetParent(parent, false); owner.transform.localPosition = position;
            var text = owner.AddComponent<TextMesh>(); text.text = caption; text.anchor = TextAnchor.MiddleCenter;
            text.fontSize = GameConstants.OutpostLabelFontSize; text.characterSize = GameConstants.OutpostLabelSize;
            text.color = TerrainGenerator.ColorFromHex(GameConstants.BaseColor);
            return text;
        }
        private void Refresh(int index)
        {
            if (index < 0 || index >= crops.Length) return;
            var plot = mission.Farm.Plots[index];
            crops[index].gameObject.SetActive(plot.Crop != null);
            float height = GameConstants.OutpostCropHeight * Mathf.Max(GameConstants.OutpostMinimumGrowth, (float)plot.Growth);
            crops[index].localScale = new Vector3(GameConstants.OutpostPlotWidth, height, GameConstants.OutpostPlotDepth);
            var p = crops[index].localPosition; p.y = GameConstants.BaseDeckThickness + height / 2; crops[index].localPosition = p;
            captionsDirty = true;
        }
        private void RefreshCaptions()
        {
            if (!captionsDirty) return;
            captionsDirty = false;
            for (int index = 0; index < captions.Length; index++)
            {
            if (captions[index] == null) continue;
            var plot = mission.Farm.Plots[index];
            captions[index].text = plot.Crop == null ? "EMPTY / TEND FARM" : plot.Ready ? "READY / HARVEST"
                : plot.Crop.Name + " " + (plot.Growth * GameConstants.StatMaximum).ToString("0") + "%";
            }
        }
        private void Subscribe()
        {
            if (mission == null || subscribed) return;
            mission.Simulation.Events.FarmPlotChanged += Refresh; subscribed = true;
            if (Application.isPlaying) InvokeRepeating(nameof(RefreshCaptions), GameConstants.UiRefreshSeconds, GameConstants.UiRefreshSeconds);
        }
        private void OnEnable() { Subscribe(); }
        private void OnDisable()
        {
            CancelInvoke();
            if (!subscribed) return;
            mission.Simulation.Events.FarmPlotChanged -= Refresh; subscribed = false;
        }
    }
}
