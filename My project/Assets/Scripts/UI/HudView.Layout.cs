using System;
using MarsTrainer.Data.Research;
using MarsTrainer.Sim;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MarsTrainer.UI
{
    public sealed partial class HudView
    {
        public static GameObject Create()
        {
#if UNITY_EDITOR
            EnsureFont();
#endif
            var owner = new GameObject("Mars Trainer HUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = owner.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 10;
            var scaler = owner.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(GameConstants.UiWidth, GameConstants.UiHeight); scaler.matchWidthOrHeight = .5f;
            var hud = owner.AddComponent<HudView>();
            hud.safeArea = StatBar.Frame(owner.transform, "Safe area", 0, 0, 1, 1);
            var game = StatBar.Frame(hud.safeArea, "Mission HUD", 0, 0, 1, 1);
            hud.gameplay = game.gameObject.AddComponent<CanvasGroup>();
            var heading = StatBar.Frame(game, "Mission date", .02f, .91f, .30f, .98f, GameConstants.UiSurface); StatBar.Stack(heading);
            hud.date = StatBar.Text(heading, "Martian Days 00.0 / " + GameConstants.MissionDurationDays, height: GameConstants.UiLine);
            var location = StatBar.Frame(game, "Navigation", .32f, .925f, .66f, .995f, GameConstants.UiSurface); StatBar.Stack(location);
            hud.navigation = StatBar.Text(location, "Base 0 m"); hud.navigation.alignment = TextAlignmentOptions.Center;
            var menus = StatBar.Frame(game, "Screen buttons", .72f, .91f, .98f, .985f);
            var row = menus.gameObject.AddComponent<HorizontalLayoutGroup>(); row.spacing = GameConstants.UiGap;
            row.childControlHeight = row.childForceExpandHeight = row.childControlWidth = row.childForceExpandWidth = true;
            hud.kitButton = StatBar.Button(menus, "Pack [E]", null);
            ResourceGlyph.Decorate(hud.kitButton, ResourceSymbol.Backpack);
            hud.pauseButton = StatBar.Button(menus, "Menu [Esc]", null); hud.pauseButton.name = "Menu [Escape]";
            ResourceGlyph.Decorate(hud.pauseButton, ResourceSymbol.Menu);
            var survival = StatBar.Frame(game, "Suit vitals", .02f, .025f, .25f, .34f);
            var strip = survival.gameObject.AddComponent<VerticalLayoutGroup>();
            strip.spacing = GameConstants.UiHudVitalGap; strip.childControlWidth = strip.childControlHeight = true;
            strip.childForceExpandHeight = false; strip.childForceExpandWidth = true;
            hud.stats = new StatBar[5]; string[] labels = { "Health", "Food", "Water", "Rest", "Air" };
            int[] priority = {4,0,2,1,3};
            foreach (int i in priority) hud.stats[i] = StatBar.Create(survival, labels[i], true);
            var resources = StatBar.Frame(game, "Suit power", .72f, .17f, .98f, .245f, GameConstants.UiSurface); StatBar.Stack(resources);
            hud.fieldStatus = StatBar.Text(resources, "Power 60 kWh / Radiation Safe");
            var hint = StatBar.Frame(game, "Walking controls", .31f, .025f, .70f, .17f, GameConstants.UiSurface);
            hud.walkingHint = hint.gameObject;
            ControlGuide.Create(hint, "Walking Controls", new[,] {{"WASD", "Walk"}, {"RMB", "Look"}, {"E", "Open Pack"}});
            hud.alerts = AlertBanner.Create(game);
            var kit = StatBar.Frame(game, "Field kit", .30f, .13f, .98f, .87f, GameConstants.UiSurface);
            kit.GetComponent<Image>().raycastTarget = true; hud.fieldKit = kit.gameObject;
            var title = StatBar.Frame(kit, "Kit heading", .03f, .91f, .58f, .98f);
            StatBar.Text(title, "Expedition Pack", GameConstants.UiHeading);
            hud.researchButton = StatBar.Button(kit, "Research [R]", null); Place(hud.researchButton, .60f, .90f, .79f, .985f);
            hud.closeKitButton = StatBar.Button(kit, "Close [E]", null); Place(hud.closeKitButton, .81f, .90f, .97f, .985f);
            hud.time = TimeControls.Create(kit); Place(hud.time, .03f, .57f, .49f, .89f);
            var power = StatBar.Frame(kit, "Power", .03f, .31f, .49f, .55f, GameConstants.UiPanel); StatBar.Stack(power);
            hud.battery = StatBar.Create(power, "Battery");
            hud.energy = StatBar.Text(power, "In -- / Out -- kWh\nPer Martian Day", height: GameConstants.UiLine * 1.5f);
            hud.sunZap = SunZapMeterView.Create(kit);
            hud.tutorial = TutorialPanel.Create(kit);
            hud.missionControls = MissionControls.Create(kit);
            var supplies=StatBar.Frame(game,"Quick supplies",.79f,.025f,.98f,.15f);
            var supplyRow=supplies.gameObject.AddComponent<HorizontalLayoutGroup>(); supplyRow.spacing=GameConstants.UiGap;
            supplyRow.childControlWidth=supplyRow.childControlHeight=supplyRow.childForceExpandWidth=supplyRow.childForceExpandHeight=true;
            hud.eatButton=StatBar.Button(supplies,"Eat [F]",null);
            hud.drinkButton=StatBar.Button(supplies,"Drink [G]",null);
            hud.eatCaption=hud.eatButton.GetComponentInChildren<TMP_Text>();
            hud.drinkCaption=hud.drinkButton.GetComponentInChildren<TMP_Text>();
            ResourceGlyph.Create(hud.eatButton.transform,ResourceSymbol.Food,GameConstants.UiBadgeDiameter);
            ResourceGlyph.Create(hud.drinkButton.transform,ResourceSymbol.Water,GameConstants.UiBadgeDiameter);
            foreach (var button in new[]{hud.eatButton,hud.drinkButton})
            {
                var glyph=button.GetComponentInChildren<ResourceGlyph>().rectTransform;
                glyph.anchorMin=glyph.anchorMax=new Vector2(.5f,.73f); glyph.pivot=new Vector2(.5f,.5f); glyph.anchoredPosition=Vector2.zero;
                var text=button.GetComponentInChildren<TMP_Text>().rectTransform;
                text.anchorMin=new Vector2(0,0);text.anchorMax=new Vector2(1,.53f);
                text.offsetMin=text.offsetMax=Vector2.zero;
            }
            kit.gameObject.SetActive(false);
            hud.research = ResearchScreen.Create(hud.safeArea); hud.pause = PauseMenu.Create(hud.safeArea); hud.end = EndScreen.Create(hud.safeArea);
            hud.briefing = MissionBriefing.Create(hud.safeArea);
            return owner;
        }
        private static void Place(Component component, float x0, float y0, float x1, float y1)
        {
            var rect = (RectTransform)component.transform;
            rect.anchorMin = new Vector2(x0, y0); rect.anchorMax = new Vector2(x1, y1);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
#if UNITY_EDITOR
        private static void EnsureFont()
        {
            const string folder = "Assets/Resources";
            if (!UnityEditor.AssetDatabase.IsValidFolder(folder)) UnityEditor.AssetDatabase.CreateFolder("Assets", "Resources");
            if (Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF") == null)
                throw new InvalidOperationException("Import TextMeshPro Essential Resources before building the HUD prefabs.");
            UiTypeface.EnsureAsset();
        }
        [UnityEditor.MenuItem("Mars Trainer/Build P10 UI Prefabs")]
        public static void BuildPrefab()
        {
            var root = Create(); var hud = root.GetComponent<HudView>();
            var ids = UnityEditor.AssetDatabase.FindAssets("t:ResearchNodeSO", new[] { "Assets/Data/Research" });
            Array.Sort(ids, StringComparer.Ordinal);
            hud.catalog = new ResearchNodeSO[ids.Length];
            for (int i = 0; i < ids.Length; i++) hud.catalog[i] = UnityEditor.AssetDatabase.LoadAssetAtPath<ResearchNodeSO>(UnityEditor.AssetDatabase.GUIDToAssetPath(ids[i]));
            Array.Sort(hud.catalog, (a, b) => a.path != b.path ? a.path.CompareTo(b.path) : a.tier != b.tier ? a.tier.CompareTo(b.tier) : string.CompareOrdinal(a.id, b.id));
            UnityEditor.PrefabUtility.SaveAsPrefabAsset(root, "Assets/Resources/MarsTrainerHUD.prefab");
            if (!UnityEditor.AssetDatabase.IsValidFolder("Assets/Prefabs")) UnityEditor.AssetDatabase.CreateFolder("Assets", "Prefabs");
            if (!UnityEditor.AssetDatabase.IsValidFolder("Assets/Prefabs/UI")) UnityEditor.AssetDatabase.CreateFolder("Assets/Prefabs", "UI");
            UnityEditor.PrefabUtility.SaveAsPrefabAsset(hud.research.gameObject, "Assets/Prefabs/UI/ResearchScreen.prefab");
            UnityEditor.PrefabUtility.SaveAsPrefabAsset(hud.pause.gameObject, "Assets/Prefabs/UI/PauseMenu.prefab");
            UnityEditor.PrefabUtility.SaveAsPrefabAsset(hud.end.gameObject, "Assets/Prefabs/UI/EndScreen.prefab");
            DestroyImmediate(root); UnityEditor.AssetDatabase.SaveAssets();
        }
#endif
    }
}
