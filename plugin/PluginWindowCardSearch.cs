﻿using Dalamud;
using Dalamud.Game.Gui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Windowing;
using FFTriadBuddy;
using ImGuiNET;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace TriadBuddyPlugin
{
    public unsafe class PluginWindowCardSearch : Window, IDisposable
    {
        private const float WindowContentWidth = 270.0f;

        private readonly UIReaderTriadCardList uiReaderCardList;
        private readonly GameGui gameGui;
        private readonly Configuration config;

        private List<Tuple<TriadCard, GameCardInfo>> listCards = new();
        private List<Tuple<TriadNpc, GameNpcInfo>> listNpcs = new();
        private List<Tuple<TriadCard, int>> listNpcReward = new();
        private int numNotOwnedRewards = 0;

        private int selectedCardIdx;
        private int selectedNpcIdx;
        private int filterMode = -1;
        private ImGuiTextFilterPtr searchFilterCard;
        private ImGuiTextFilterPtr searchFilterNpc;

        private bool showNpcMatchesOnly = false;
        private bool showNotOwnedOnly = false;
        private bool hideNpcBeatenOnce = false;
        private bool hideNpcCompleted = false;

        private string locNpcOnly;
        private string locNotOwnedOnly;
        private string locFilterActive;
        private string locHideBeatenNpc;
        private string locHideCompletedNpc;
        private string locTabCards;
        private string locTabNpc;
        private string locNpcReward;
        private string locShowOnMap;
        private string locNoAvail;

        public PluginWindowCardSearch(UIReaderTriadCardList uiReaderCardList, GameGui gameGui, Configuration config) : base("Card Search")
        {
            this.uiReaderCardList = uiReaderCardList;
            this.gameGui = gameGui;
            this.config = config;

            var searchFilterCardPtr = ImGuiNative.ImGuiTextFilter_ImGuiTextFilter(null);
            searchFilterCard = new ImGuiTextFilterPtr(searchFilterCardPtr);

            var searchFilterNpcPtr = ImGuiNative.ImGuiTextFilter_ImGuiTextFilter(null);
            searchFilterNpc = new ImGuiTextFilterPtr(searchFilterNpcPtr);

            uiReaderCardList.OnVisibilityChanged += (_) => UpdateWindowData();
            uiReaderCardList.OnUIStateChanged += OnUIStateChanged;
            UpdateWindowData();

            if (config != null)
            {
                showNpcMatchesOnly = config.CheckCardNpcMatchOnly;
                showNotOwnedOnly = config.CheckCardNotOwnedOnly;
                hideNpcBeatenOnce = config.CheckNpcHideBeaten;
                hideNpcCompleted = config.CheckNpcHideCompleted;
            }

            // doesn't matter will be updated on next draw
            PositionCondition = ImGuiCond.None;
            SizeCondition = ImGuiCond.Always;

            SizeConstraints = new WindowSizeConstraints() { MinimumSize = new Vector2(WindowContentWidth + 20, 0), MaximumSize = new Vector2(WindowContentWidth + 20, 1000) };

            RespectCloseHotkey = false;
            Flags = ImGuiWindowFlags.NoDecoration |
                //ImGuiWindowFlags.NoResize |
                ImGuiWindowFlags.AlwaysAutoResize |
                ImGuiWindowFlags.NoSavedSettings |
                ImGuiWindowFlags.NoMove |
                //ImGuiWindowFlags.NoMouseInputs |
                ImGuiWindowFlags.NoDocking |
                ImGuiWindowFlags.NoFocusOnAppearing |
                ImGuiWindowFlags.NoNav;

            Plugin.CurrentLocManager.LocalizationChanged += (_) => CacheLocalization();
            CacheLocalization();
        }

        public void Dispose()
        {
            ImGuiNative.ImGuiTextFilter_destroy(searchFilterCard.NativePtr);
            ImGuiNative.ImGuiTextFilter_destroy(searchFilterNpc.NativePtr);
        }

        private void CacheLocalization()
        {
            locNpcOnly = Localization.Localize("CS_NpcOnly", "NPC matches only");
            locNotOwnedOnly = Localization.Localize("CS_NotOwnedOnly", "Not owned only");
            locFilterActive = Localization.Localize("CS_FilterActive", "(Collection filtering is active)");
            locHideBeatenNpc = Localization.Localize("CS_BeatenNpc", "Hide beaten once");
            locHideCompletedNpc = Localization.Localize("CS_CompletedNpc", "Hide completed");
            locTabCards = Localization.Localize("CS_TabCards", "Cards");
            locTabNpc = Localization.Localize("CS_TabNpc", "NPC");

            // reuse CardInfo locs
            locNpcReward = Localization.Localize("CI_NpcReward", "NPC reward:");
            locShowOnMap = Localization.Localize("CI_ShowMap", "Show on map");
            locNoAvail = Localization.Localize("CI_NotAvail", "Not available");
        }

        private void UpdateWindowData()
        {
            bool wasOpen = IsOpen;
            IsOpen = uiReaderCardList.IsVisible;

            if (IsOpen && !wasOpen)
            {
                GameCardDB.Get().Refresh();
                filterMode = -1;
                searchFilterCard.Clear();

                GameNpcDB.Get().Refresh();
                searchFilterNpc.Clear();

                OnUIStateChanged(uiReaderCardList.cachedState);
                GenerateNpcList();
            }
        }

        public void OnUIStateChanged(UIStateTriadCardList uiState)
        {
            if (filterMode != uiState.filterMode)
            {
                filterMode = uiState.filterMode;
                listCards.Clear();

                var cardDB = TriadCardDB.Get();
                var cardInfoDB = GameCardDB.Get();

                bool includeOwned = filterMode != 2;
                bool includeMissing = filterMode != 1;

                foreach (var card in cardDB.cards)
                {
                    if (card != null && card.IsValid())
                    {
                        var cardInfo = cardInfoDB.FindById(card.Id);
                        if (cardInfo != null)
                        {
                            if ((includeOwned && cardInfo.IsOwned) || (includeMissing && !cardInfo.IsOwned))
                            {
                                listCards.Add(new Tuple<TriadCard, GameCardInfo>(card, cardInfo));
                            }
                        }
                    }
                }

                if (listCards.Count > 1)
                {
                    listCards.Sort((a, b) => a.Item1.Name.GetLocalized().CompareTo(b.Item1.Name.GetLocalized()));
                }

                selectedCardIdx = -1;
            }
        }

        public override void PreDraw()
        {
            Position = new Vector2(uiReaderCardList.cachedState.screenPos.X + uiReaderCardList.cachedState.screenSize.X + 10, uiReaderCardList.cachedState.screenPos.Y);
        }

        public override void Draw()
        {
            if (ImGui.BeginTabBar("##CollectionSearch", ImGuiTabBarFlags.None))
            {
                if (ImGui.BeginTabItem(locTabCards))
                {
                    DrawCardsTab();
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem(locTabNpc))
                {
                    DrawNpcTab();
                    ImGui.EndTabItem();
                }

                ImGui.EndTabBar();
            }
        }

        private void DrawCardsTab()
        {
            bool showOwnedCheckbox = filterMode == 0;
            searchFilterCard.Draw("", WindowContentWidth * ImGuiHelpers.GlobalScale);

            if (ImGui.BeginListBox("##cards", new Vector2(WindowContentWidth * ImGuiHelpers.GlobalScale, ImGui.GetTextLineHeightWithSpacing() * 10)))
            {
                for (int idx = 0; idx < listCards.Count; idx++)
                {
                    var (cardOb, cardInfo) = listCards[idx];
                    if ((showNpcMatchesOnly && cardInfo.RewardNpcs.Count <= 0) ||
                        (showOwnedCheckbox && showNotOwnedOnly && cardInfo.IsOwned))
                    {
                        continue;
                    }

                    var itemDesc = cardOb.Name.GetLocalized();
                    if (searchFilterCard.PassFilter(itemDesc))
                    {
                        bool isSelected = selectedCardIdx == idx;
                        if (ImGui.Selectable($"{(int)cardOb.Rarity + 1}★   {itemDesc}", isSelected))
                        {
                            selectedCardIdx = idx;
                            OnCardSelectionChanged();
                        }

                        if (isSelected)
                        {
                            ImGui.SetItemDefaultFocus();
                        }
                    }
                }
                ImGui.EndListBox();
            }

            ImGui.Spacing();
            if (ImGui.Checkbox(locNpcOnly, ref showNpcMatchesOnly))
            {
                if (config != null)
                {
                    config.CheckCardNpcMatchOnly = showNpcMatchesOnly;
                    config.Save();
                }
            }

            if (showOwnedCheckbox)
            {
                if (ImGui.Checkbox(locNotOwnedOnly, ref showNotOwnedOnly))
                {
                    if (config != null)
                    {
                        config.CheckCardNotOwnedOnly = showNotOwnedOnly;
                        config.Save();
                    }
                }
            }
            else
            {
                ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1), locFilterActive);
            }
        }

        private void DrawNpcTab()
        {
            searchFilterNpc.Draw("", WindowContentWidth * ImGuiHelpers.GlobalScale);

            if (ImGui.BeginListBox("##npcs", new Vector2(WindowContentWidth * ImGuiHelpers.GlobalScale, ImGui.GetTextLineHeightWithSpacing() * 10)))
            {
                for (int idx = 0; idx < listNpcs.Count; idx++)
                {
                    var (npcOb, npcInfo) = listNpcs[idx];
                    if ((hideNpcBeatenOnce && (npcInfo.IsBeatenOnce || npcInfo.IsExcludedFromAchievementTracker)) ||
                        (hideNpcCompleted && npcInfo.IsCompleted))
                    {
                        continue;
                    }

                    var itemDesc = npcOb.Name.GetLocalized();
                    if (searchFilterNpc.PassFilter(itemDesc))
                    {
                        bool isSelected = selectedNpcIdx == idx;
                        if (ImGui.Selectable(itemDesc, isSelected))
                        {
                            selectedNpcIdx = idx;
                            GenerateNpcRewardList();
                        }

                        if (isSelected)
                        {
                            ImGui.SetItemDefaultFocus();
                        }
                    }
                }
                ImGui.EndListBox();
            }

            ImGui.Spacing();
            if (ImGui.Checkbox(locHideBeatenNpc, ref hideNpcBeatenOnce))
            {
                if (config != null)
                {
                    config.CheckNpcHideBeaten = hideNpcBeatenOnce;
                    config.Save();
                }
            }

            if (ImGui.Checkbox(locHideCompletedNpc, ref hideNpcCompleted))
            {
                if (config != null)
                {
                    config.CheckNpcHideCompleted = hideNpcCompleted;
                    config.Save();
                }
            }

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            DrawNpcDetails();
        }

        private void DrawNpcDetails()
        {
            var npcData = (selectedNpcIdx < 0 || selectedNpcIdx >= listNpcs.Count) ? null : listNpcs[selectedNpcIdx];
            if (npcData != null && npcData.Item2 != null)
            {
                var cursorY = ImGui.GetCursorPosY();
                ImGui.SetCursorPosY(cursorY - ImGui.GetStyle().FramePadding.Y);
                if (ImGuiComponents.IconButton(Dalamud.Interface.FontAwesomeIcon.Map))
                {
                    gameGui.OpenMapWithMapLink(npcData.Item2.Location);
                }
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip(locShowOnMap);
                }

                ImGui.SetCursorPosY(cursorY);
                ImGui.SameLine();
                ImGui.Text($"{npcData.Item2.Location.PlaceName} {npcData.Item2.Location.CoordinateString}");

                ImGui.Spacing();

                ImGui.Text($"{locNpcReward} {numNotOwnedRewards}");
                if (listNpcReward.Count > 0)
                {
                    var settingsDB = PlayerSettingsDB.Get();

                    ImGui.BeginListBox("##cardReward", new Vector2(WindowContentWidth * ImGuiHelpers.GlobalScale, ImGui.GetTextLineHeightWithSpacing() * 4.5f));
                    for (int idx = 0; idx < listNpcReward.Count; idx++)
                    {
                        var (cardOb, cardListIdx) = listNpcReward[idx];
                        bool isCardOwned = settingsDB.ownedCards.Contains(cardOb);

                        var itemDesc = cardOb.Name.GetLocalized();
                        bool isSelected = selectedCardIdx == cardListIdx;

                        if (isCardOwned)
                        {
                            ImGui.PushStyleColor(ImGuiCol.Text, 0xffa8a8a8);
                        }

                        if (ImGui.Selectable($"{(int)cardOb.Rarity + 1}★   {itemDesc}", isSelected))
                        {
                            selectedCardIdx = cardListIdx;
                            OnCardSelectionChanged();
                        }

                        if (isCardOwned)
                        {
                            ImGui.PopStyleColor(1);
                        }

                        if (isSelected)
                        {
                            ImGui.SetItemDefaultFocus();
                        }
                    }
                    ImGui.EndListBox();
                }
                else
                {
                    var colorGray = new Vector4(0.6f, 0.6f, 0.6f, 1);
                    ImGui.TextColored(colorGray, locNoAvail);
                }
            }
        }

        private void OnCardSelectionChanged()
        {
            var (cardOb, cardInfo) = (selectedCardIdx >= 0) && (selectedCardIdx < listCards.Count) ? listCards[selectedCardIdx] : null;
            if (cardOb != null && cardInfo != null)
            {
                var filterEnum = (filterMode == 1) ? GameCardCollectionFilter.OnlyOwned :
                    (filterMode == 2) ? GameCardCollectionFilter.OnlyMissing :
                    GameCardCollectionFilter.All;

                var collectionPos = cardInfo.Collection[(int)filterEnum];

                //PluginLog.Log($"Card selection! {cardOb.Name.GetLocalized()}, filter:{filterEnum} ({filterMode}) => page:{collectionPos.PageIndex}, cell:{collectionPos.CellIndex}");
                uiReaderCardList.SetPageAndGridView(collectionPos.PageIndex, collectionPos.CellIndex);
            }
        }

        private void GenerateNpcList()
        {
            listNpcs.Clear();

            var npcDB = TriadNpcDB.Get();
            var npcInfoDB = GameNpcDB.Get();

            foreach (var kvp in npcInfoDB.mapNpcs)
            {
                var npc = (kvp.Key >= 0 && kvp.Key < npcDB.npcs.Count) ? npcDB.npcs[kvp.Key] : null;
                if (npc != null)
                {
                    listNpcs.Add(new Tuple<TriadNpc, GameNpcInfo>(npc, kvp.Value));
                }
            }

            if (listNpcs.Count > 1)
            {
                listNpcs.Sort((a, b) => a.Item1.Name.GetLocalized().CompareTo(b.Item1.Name.GetLocalized()));
            }

            selectedNpcIdx = -1;
        }

        private void GenerateNpcRewardList()
        {
            listNpcReward.Clear();

            numNotOwnedRewards = 0;
            var settingsDB = PlayerSettingsDB.Get();

            var npcData = (selectedNpcIdx < 0 || selectedNpcIdx >= listNpcs.Count) ? null : listNpcs[selectedNpcIdx];
            if (npcData != null && npcData.Item2 != null)
            {
                foreach (var cardId in npcData.Item2.rewardCards)
                {
                    int listIdx = listCards.FindIndex(x => x.Item1.Id == cardId);
                    if (listIdx >= 0)
                    {
                        var cardOb = listCards[listIdx].Item1;
                        if (cardOb != null)
                        {
                            listNpcReward.Add(new Tuple<TriadCard, int>(cardOb, listIdx));
                            numNotOwnedRewards += settingsDB.ownedCards.Contains(cardOb) ? 0 : 1;
                        }
                    }
                }
            }

            if (listNpcReward.Count > 1)
            {
                listNpcReward.Sort((a, b) => a.Item1.Name.GetLocalized().CompareTo(b.Item1.Name.GetLocalized()));
            }
        }
    }
}
