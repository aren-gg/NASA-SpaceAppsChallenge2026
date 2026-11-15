using System;
using MarsTrainer.Data.Research;
using MarsTrainer.Sim;
using MarsTrainer.Sim.Research;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MarsTrainer.UI
{
    public sealed class ResearchScreen : MonoBehaviour
    {
        [SerializeField] private RectTransform[] paths;
        [SerializeField] private TMP_Text points;
        [SerializeField] private TMP_Text details;
        [SerializeField] private Button buy;
        [SerializeField] private Button close;
        private ResearchNodeSO[] catalog = Array.Empty<ResearchNodeSO>();
        private ResearchSystem source;
        private Button[] cards = Array.Empty<Button>();
        private ResearchNodeSO selected;
        public Action Closed { get; set; }
        public Action<string> PurchaseRequested { get; set; }
        public void Bind(ResearchSystem research, ResearchNodeSO[] nodes)
        {
            source = research; catalog = nodes ?? Array.Empty<ResearchNodeSO>();
            for (int p = 0; p < paths.Length; p++)
                for (int c = paths[p].childCount - 1; c >= 0; c--)
                {
                    var old = paths[p].GetChild(c).gameObject; old.SetActive(false);
                    if (Application.isPlaying) Destroy(old); else DestroyImmediate(old);
                }
            cards = new Button[catalog.Length];
            for (int i = 0; i < catalog.Length; i++)
            {
                var node = catalog[i]; if (node == null) continue;
                var card = StatBar.Button(paths[(int)node.path], node.title, () => Select(node));
                card.GetComponent<LayoutElement>().minHeight = GameConstants.UiCard;
                card.GetComponent<LayoutElement>().preferredHeight = GameConstants.UiCard;
                var trigger = card.gameObject.AddComponent<EventTrigger>();
                AddTrigger(trigger, EventTriggerType.Select, () => { Select(node); Reveal(card); });
                AddTrigger(trigger, EventTriggerType.PointerEnter, () => Select(node));
                var scroll = card.GetComponentInParent<ScrollRect>(true);
                ForwardPointer(trigger, EventTriggerType.Scroll, data => scroll.OnScroll(data));
                ForwardPointer(trigger, EventTriggerType.InitializePotentialDrag, data => scroll.OnInitializePotentialDrag(data));
                ForwardPointer(trigger, EventTriggerType.BeginDrag, data => scroll.OnBeginDrag(data));
                ForwardPointer(trigger, EventTriggerType.Drag, data => scroll.OnDrag(data));
                ForwardPointer(trigger, EventTriggerType.EndDrag, data => scroll.OnEndDrag(data));
                cards[i] = card;
            }
            close.onClick.RemoveAllListeners(); close.onClick.AddListener(() => Closed?.Invoke());
            buy.onClick.RemoveAllListeners(); buy.onClick.AddListener(Purchase);
            Refresh();
        }
        private static void AddTrigger(EventTrigger trigger, EventTriggerType type, Action action)
        {
            var entry = new EventTrigger.Entry { eventID = type };
            entry.callback.AddListener(_ => action()); trigger.triggers.Add(entry);
        }
        // EventTrigger consumes pointer events, so pass scrolling and dragging to the path.
        private static void ForwardPointer(EventTrigger trigger, EventTriggerType type, Action<PointerEventData> action)
        {
            var entry = new EventTrigger.Entry { eventID = type };
            entry.callback.AddListener(data => action((PointerEventData)data)); trigger.triggers.Add(entry);
        }
        private static void Reveal(Button card)
        {
            var scroll = card.GetComponentInParent<ScrollRect>(true);
            int count = card.transform.parent.childCount;
            if (scroll != null && count > 1) scroll.verticalNormalizedPosition = 1f - (float)card.transform.GetSiblingIndex() / (count - 1);
        }
        public void Open() { gameObject.SetActive(true); Refresh(); close.Select(); }
        public void Refresh()
        {
            points.text = source == null ? "Research is not connected yet" : "Research Points: " + source.State.RP.ToString("0.0");
            for (int i = 0; i < cards.Length; i++)
            {
                if (cards[i] == null) continue;
                var node = catalog[i]; bool owned = source != null && source.State.IsPurchased(node.id);
                bool available = source != null && source.CanPurchase(node.id);
                cards[i].GetComponentInChildren<TMP_Text>().text = node.title + "\n" +
                    (owned ? "O  Owned" : available ? "+  Available" : "X  Locked") + " | " + node.costRP.ToString("0.#") + " RP";
            }
            if (selected != null) Select(selected); else buy.interactable = false;
        }
        private void Select(ResearchNodeSO node)
        {
            selected = node;
            details.text = "Why This Matters: " + node.description + "\nCosts " + node.costRP.ToString("0.#")
                + " RP; Purchase Energy: 0 kWh. Gains: " + node.title + ".";
            if (source != null && !source.CanPurchase(node.id) && !source.State.IsPurchased(node.id))
                details.text += "\nLocked: collect more points and finish earlier cards first.";
            buy.interactable = source != null && source.CanPurchase(node.id);
        }
        private void Purchase() { if (selected != null) PurchaseRequested?.Invoke(selected.id); Refresh(); }
        public static ResearchScreen Create(Transform parent)
        {
            var panel = StatBar.Frame(parent, "Research", 0, 0, 1, 1, GameConstants.UiSurface);
            panel.GetComponent<Image>().raycastTarget = true;
            var view = panel.gameObject.AddComponent<ResearchScreen>();
            var header = StatBar.Frame(panel, "Heading", 0.02f, 0.84f, 0.72f, 0.98f); StatBar.Stack(header);
            StatBar.Text(header, "Build a Safer Outpost", GameConstants.UiTitle, GameConstants.UiButton);
            view.points = StatBar.Text(header, "Research Points: --");
            var closeSlot = StatBar.Frame(panel, "Close slot", 0.76f, 0.88f, 0.98f, 0.97f); StatBar.Stack(closeSlot);
            view.close = StatBar.Button(closeSlot, "Back [Escape]", null);
            view.paths = new RectTransform[4];
            for (int p = 0; p < view.paths.Length; p++)
            {
                float x = p / (float)view.paths.Length;
                var column = StatBar.Frame(panel, ((ResearchPath)p).ToString(), x + 0.01f, 0.34f, x + 0.24f, 0.84f, GameConstants.UiPanel);
                var heading = StatBar.Frame(column, "Path name", 0.04f, 0.86f, 0.96f, 0.98f);
                StatBar.Text(heading, ((ResearchPath)p).ToString(), GameConstants.UiHeading);
                var viewport = StatBar.Frame(column, "Scrollable cards", 0.02f, 0.02f, 0.98f, 0.84f);
                viewport.gameObject.AddComponent<RectMask2D>(); var scroll = viewport.gameObject.AddComponent<ScrollRect>();
                var content = StatBar.Frame(viewport, "Cards", 0, 1, 1, 1); content.pivot = new Vector2(0.5f, 1);
                StatBar.Stack(content); var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
                fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                scroll.viewport = viewport; scroll.content = content; scroll.horizontal = false;
                scroll.scrollSensitivity = GameConstants.UiLine; view.paths[p] = content;
                viewport.gameObject.AddComponent<Image>().color = Color.clear;
            }
            var detail = StatBar.Frame(panel, "Choice details", 0.02f, 0.02f, 0.98f, 0.32f); StatBar.Stack(detail);
            view.details = StatBar.Text(detail, "Choose a card with your mouse or arrow keys. Scroll each path to see more.\nEvery card explains its cost and what it helps you do.", height: GameConstants.UiCard + GameConstants.UiLine);
            view.buy = StatBar.Button(detail, "Buy selected upgrade", null);
            panel.gameObject.SetActive(false); return view;
        }
    }
}
