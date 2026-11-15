using System.Collections.Generic;
using MarsTrainer.UI;
using MarsTrainer.Sim;
using MarsTrainer.Sim.Research;
using MarsTrainer.Data.Research;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace MarsTrainer.Tests
{
    public sealed class HudScreenTests
    {
        private GameObject root;
        [TearDown] public void Cleanup() { if (root != null) Object.DestroyImmediate(root); }

        [TestCase(1280, 720, false)]
        [TestCase(1920, 1080, false)]
        [TestCase(1280, 720, true)]
        [TestCase(1920, 1080, true)]
        public void AllScreenTextFitsItsAllocatedSpace(int width, int height, bool largeText)
        {
            root = HudView.Create();
            root.GetComponent<HudView>().Bind(new Simulation());
            root.GetComponentInChildren<MissionSettings>(true).SetLargeText(largeText);
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var rect = (RectTransform)root.transform;
            rect.sizeDelta = new Vector2(1920, 1080);
            root.transform.localScale = Vector3.one * width / 1920f;
            foreach (var text in root.GetComponentsInChildren<TMP_Text>(true))
            {
                // Activate one modal at a time so inactive layout groups are also measured.
                var parent = text.transform.parent;
                while (parent != null && parent != root.transform) { parent.gameObject.SetActive(true); parent = parent.parent; }
            }
            Canvas.ForceUpdateCanvases();
            foreach (var layout in root.GetComponentsInChildren<VerticalLayoutGroup>(true))
                LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)layout.transform);
            Canvas.ForceUpdateCanvases();
            var failures = new List<string>();
            foreach (var group in root.GetComponentsInChildren<VerticalLayoutGroup>(true))
            {
                var bounds = ((RectTransform)group.transform).rect;
                float used = group.padding.top + group.padding.bottom;
                int count = 0;
                foreach (Transform child in group.transform)
                {
                    if (!child.gameObject.activeSelf) continue;
                    var element = child.GetComponent<LayoutElement>();
                    if (element != null && !element.ignoreLayout) { used += element.preferredHeight; count++; }
                }
                used += Mathf.Max(0, count - 1) * group.spacing;
                if (used > bounds.height + 1) failures.Add(group.name + " vertical overflow: " + used + " > " + bounds.height);
            }
            foreach (var text in root.GetComponentsInChildren<TMP_Text>(true))
            {
                Assert.That(text.font, Is.Not.Null, text.name + " missing font");
                Assert.That(text.font.name, Is.EqualTo("CounterStrike UI SDF"));
                Assert.That(text.font.characterLookupTable.ContainsKey(45), Is.False, "Hyphens must use readable fallback punctuation.");
                text.ForceMeshUpdate();
                Assert.That(text.mesh.vertexCount, Is.GreaterThan(0), text.name + " missing text geometry");
                if (text.isTextOverflowing) failures.Add(text.name + ": " + text.text);
                Assert.That(text.fontSize * width / 1920f, Is.GreaterThanOrEqualTo(16), text.name);
            }
            Assert.That(failures, Is.Empty);
        }

        [Test]
        public void AuthoredResearchCardsHaveReadableDetailsAndPurchasesUpdateState()
        {
            root = Object.Instantiate(Resources.Load<GameObject>("MarsTrainerHUD"));
            var ids = UnityEditor.AssetDatabase.FindAssets("t:ResearchNodeSO", new[] { "Assets/Data/Research" });
            var definitions = new ResearchNodeSO[ids.Length]; var nodes = new ResearchNode[ids.Length];
            for (int i = 0; i < ids.Length; i++)
            {
                definitions[i] = UnityEditor.AssetDatabase.LoadAssetAtPath<ResearchNodeSO>(UnityEditor.AssetDatabase.GUIDToAssetPath(ids[i]));
                nodes[i] = definitions[i].ToNode();
            }
            var sim = new Simulation(); var source = new ResearchSystem(nodes, sim.Modifiers); source.AddRP(10000);
            root.GetComponent<HudView>().Bind(sim, source);
            var screen = root.GetComponentInChildren<ResearchScreen>(true); screen.Bind(source, definitions); screen.Open();
            Canvas.ForceUpdateCanvases();
            var details = screen.transform.Find("Choice details").GetComponentInChildren<TMP_Text>();
            foreach (var card in screen.GetComponentsInChildren<Button>())
            {
                if (card.GetComponent<UnityEngine.EventSystems.EventTrigger>() == null) continue;
                card.onClick.Invoke(); Canvas.ForceUpdateCanvases(); details.ForceMeshUpdate();
                Assert.That(details.isTextOverflowing, Is.False, details.text);
            }
            Assert.That(nodes.Length, Is.GreaterThan(0));
            ResearchNodeSO first = null;
            for (int i = 0; i < definitions.Length; i++) if (source.CanPurchase(definitions[i].id)) { first = definitions[i]; break; }
            Assert.That(first, Is.Not.Null);
            foreach (var card in screen.GetComponentsInChildren<Button>())
                if (card.name == first.title) { card.onClick.Invoke(); break; }
            screen.transform.Find("Choice details/Buy selected upgrade").GetComponent<Button>().onClick.Invoke();
            Assert.That(source.State.IsPurchased(first.id), Is.True);
        }

        [Test]
        public void RebindingDoesNotKeepOldEventSubscriptions()
        {
            root = HudView.Create();
            var hud = root.GetComponent<HudView>();
            var oldSim = new Simulation(); var current = new Simulation();
            hud.Bind(oldSim); hud.Bind(current);
            oldSim.Events.ReportFlareAlert(2, 3);
            Assert.That(root.GetComponentInChildren<AlertBanner>(true).PendingCount, Is.Zero);
            current.Events.ReportFlareAlert(2, 3);
            Assert.That(root.GetComponentInChildren<AlertBanner>(true).PendingCount, Is.EqualTo(1));
        }

        [Test]
        public void PauseMenuRestoresThePreviousPauseState()
        {
            root = HudView.Create();
            var sim = new Simulation(); root.GetComponent<HudView>().Bind(sim);
            var menu = root.GetComponentInChildren<PauseMenu>(true);
            menu.Open(); Assert.That(sim.Clock.Paused, Is.True);
            menu.Close(); Assert.That(sim.Clock.Paused, Is.False);
            sim.Clock.Paused = true;
            menu.Open(); menu.Close(); Assert.That(sim.Clock.Paused, Is.True);
        }
    }
}
