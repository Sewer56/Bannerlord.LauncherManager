﻿using Bannerlord.LauncherManager.Extensions;
using Bannerlord.LauncherManager.Localization;
using Bannerlord.LauncherManager.Models;
using Bannerlord.LauncherManager.Utils;
using Bannerlord.ModuleManager;

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace Bannerlord.LauncherManager;

partial class LauncherManagerHandler
{
    protected internal static bool IsVisible(bool isSingleplayer, ModuleInfoExtended moduleInfo) =>
        moduleInfo.IsNative() || !isSingleplayer && moduleInfo.IsMultiplayerModule || isSingleplayer && moduleInfo.IsSingleplayerModule;

    /// <summary>
    /// External<br/>
    /// </summary>
    public bool TryOrderByLoadOrder(IEnumerable<string> loadOrder, Func<string, bool> isModuleSelected, [NotNullWhen(false)] out IReadOnlyList<string>? issues,
        out IReadOnlyList<IModuleViewModel> orderedModules)
    {
        var options = GetOptions();
        return options.BetaSorting
            ? TryOrderByLoadOrderBeta(loadOrder, isModuleSelected, out issues, out orderedModules)
            : TryOrderByLoadOrderTW(loadOrder, isModuleSelected, out issues, out orderedModules);
    }

    /// <summary>
    /// Internal<br/>
    /// </summary>
    protected internal bool TryOrderByLoadOrderTW(IEnumerable<string> loadOrder, Func<string, bool> isModuleSelected, [NotNullWhen(false)] out IReadOnlyList<string>? issues,
        out IReadOnlyList<IModuleViewModel> orderedModules, bool overwriteWhenFailure = false)
    {
        var state = GetState();

        var originalOrderedModules = new List<ModuleInfoExtendedWithMetadata>();

        var moduleViewModels = GetAllModuleViewModels() ?? [];
        var moduleViewModelLookup = moduleViewModels.ToDictionary(x => x.ModuleInfoExtended.Id, x => x);

        // Load the load order modules
        foreach (var id in loadOrder)
        {
            if (!ExtendedModuleInfoCache.TryGetValue(id, out var moduleInfoExtended)) continue;
            if (!IsVisible(state.IsSingleplayer, moduleInfoExtended)) continue;
            if (moduleInfoExtended is not ModuleInfoExtendedWithMetadata moduleInfoExtendedWithPath) continue;

            originalOrderedModules.Add(moduleInfoExtendedWithPath);
        }

        // Load the rest of modules
        foreach (var moduleInfoExtended in ExtendedModuleInfoCache.Values)
        {
            if (originalOrderedModules.Contains(moduleInfoExtended)) continue;
            if (!IsVisible(state.IsSingleplayer, moduleInfoExtended)) continue;
            if (moduleInfoExtended is not ModuleInfoExtendedWithMetadata moduleInfoExtendedWithPath) continue;

            originalOrderedModules.Add(moduleInfoExtendedWithPath);
        }

        // Topological sort them
        var rawOrderedModules = ModuleSorter.TopologySort<ModuleInfoExtended>(originalOrderedModules, x => ModuleUtilities.GetDependencies(originalOrderedModules, x))
            .OfType<ModuleInfoExtendedWithMetadata>()
            .ToList();

        var existingOrderedModules = rawOrderedModules
            .Where(x => moduleViewModelLookup.ContainsKey(x.Id))
            .ToList();

        var existingOrderedViewModels = existingOrderedModules
            .Select(x => moduleViewModelLookup[x.Id])
            .ToList();

        var existingLoadOrderValidationIssues = LoadOrderChecker.IsLoadOrderCorrect(existingOrderedModules.Where(x => moduleViewModelLookup[x.Id].IsSelected).ToList()).ToList();
        if (!overwriteWhenFailure && existingLoadOrderValidationIssues.Count != 0)
        {
            issues = existingLoadOrderValidationIssues;
            orderedModules = existingOrderedViewModels;
            return false;
        }

        var loadOrderValidationIssues = LoadOrderChecker.IsLoadOrderCorrect(existingOrderedViewModels.Where(x => x.IsSelected).Select(x => x.ModuleInfoExtended).ToList()).ToList();
        if (!overwriteWhenFailure && loadOrderValidationIssues.Count != 0)
        {
            issues = loadOrderValidationIssues;
            orderedModules = existingOrderedViewModels;
            return false;
        }

        // Toggle IsSelected
        foreach (var moduleVM in existingOrderedViewModels)
        {
            if (isModuleSelected(moduleVM.ModuleInfoExtended.Id) != moduleVM.IsSelected)
                SortHelper.ToggleModuleSelection(existingOrderedViewModels, moduleViewModelLookup, moduleVM);
        }

        var providedLoadOrderValidationIssues = LoadOrderChecker.IsLoadOrderCorrect(originalOrderedModules.Where(x => moduleViewModelLookup[x.Id].IsSelected).ToList()).ToList();
        issues = providedLoadOrderValidationIssues.Count > 0 ? providedLoadOrderValidationIssues : null;
        orderedModules = existingOrderedViewModels;
        var idx = 0;
        foreach (var viewModel in orderedModules)
            viewModel.Index = idx++;
        return true;
    }

    /// <summary>
    /// Internal<br/>
    /// </summary>
    protected internal bool TryOrderByLoadOrderBeta(IEnumerable<string> loadOrder, Func<string, bool> isModuleSelected, [NotNullWhen(false)] out IReadOnlyList<string>? issues,
        out IReadOnlyList<IModuleViewModel> orderedModules)
    {
        var state = GetState();

        var originalOrderedModules = new List<ModuleInfoExtendedWithMetadata>();

        var moduleViewModels = GetAllModuleViewModels() ?? [];
        var moduleViewModelLookup = moduleViewModels.ToDictionary(x => x.ModuleInfoExtended.Id, x => x);

        // Load all modules
        foreach (var moduleInfoExtended in ExtendedModuleInfoCache.Values)
        {
            if (!IsVisible(state.IsSingleplayer, moduleInfoExtended)) continue;
            if (moduleInfoExtended is not ModuleInfoExtendedWithMetadata moduleInfoExtendedWithPath) continue;

            originalOrderedModules.Add(moduleInfoExtendedWithPath);
        }

        // Get all present modules, ignore missing
        var presentOrderedIds = loadOrder.Intersect(originalOrderedModules.Select(x => x.Id).ToHashSet()).ToList();

        var rawOrderedModules = originalOrderedModules;

        var existingOrderedModules = rawOrderedModules
            .Where(x => moduleViewModelLookup.ContainsKey(x.Id))
            .ToList();

        var existingOrderedViewModels = existingOrderedModules
            .Select(x => moduleViewModelLookup[x.Id])
            .ToList();

        // Check the present load order
        var loadOrderValidationIssues = LoadOrderChecker.IsLoadOrderCorrect(presentOrderedIds.Select(x => ExtendedModuleInfoCache[x]).ToList()).ToList();
        if (loadOrderValidationIssues.Count != 0)
        {
            issues = loadOrderValidationIssues;
            orderedModules = existingOrderedViewModels;
            return false;
        }

        var existingLoadOrderValidationIssues = LoadOrderChecker.IsLoadOrderCorrect(existingOrderedModules).ToList();
        if (existingLoadOrderValidationIssues.Count != 0)
        {
            issues = existingLoadOrderValidationIssues;
            orderedModules = existingOrderedViewModels;
            return false;
        }

        // Toggle IsSelected
        foreach (var moduleVM in existingOrderedViewModels)
        {
            if (isModuleSelected(moduleVM.ModuleInfoExtended.Id) != moduleVM.IsSelected)
                SortHelper.ToggleModuleSelection(existingOrderedViewModels, moduleViewModelLookup, moduleVM);
        }

        SortByDefault(existingOrderedViewModels);

        // Not even sure a loop is needed
        // And I'm pretty sure that this is a dumb and non optimal solution.
        // ChangeModulePosition should move any nested dependencies higher?
        var hasInvalid = true;
        var retryCount = 0;
        var retryCountMax = presentOrderedIds.Count + 1;
        while (hasInvalid && retryCount < retryCountMax)
        {
            hasInvalid = false;
            retryCount++;
            for (var i = 0; i < presentOrderedIds.Count - 1; i++)
            {
                var xId = presentOrderedIds[i];
                var yId = presentOrderedIds[i + 1];

                var xIdx = existingOrderedViewModels.IndexOf(z => z.ModuleInfoExtended.Id == xId);
                var yIdx = existingOrderedViewModels.IndexOf(z => z.ModuleInfoExtended.Id == yId);
                if (xIdx > yIdx)
                {
                    if (!SortHelper.ChangeModulePosition(existingOrderedViewModels, moduleViewModelLookup, moduleViewModelLookup[xId], yIdx))
                    {
                        if (!SortHelper.ChangeModulePosition(existingOrderedViewModels, moduleViewModelLookup, moduleViewModelLookup[yId], xIdx))
                        {
                            hasInvalid = true;
                        }
                    }
                }
            }
        }

        if (retryCount >= retryCountMax)
        {
            issues = new[] { new BUTRTextObject("{=sLf3eIpH}Failed to order the module list!").ToString() };
            orderedModules = existingOrderedViewModels;
            return false;
        }

        var providedLoadOrderValidationIssues = LoadOrderChecker.IsLoadOrderCorrect(originalOrderedModules).ToList();
        issues = providedLoadOrderValidationIssues.Count > 0 ? providedLoadOrderValidationIssues : null;
        orderedModules = existingOrderedViewModels;
        var idx = 0;
        foreach (var viewModel in orderedModules)
            viewModel.Index = idx++;
        return true;
    }

    /// <summary>
    /// Internal<br/>
    /// </summary>
    protected internal static void SortByDefault(List<IModuleViewModel> modules)
    {
        static IEnumerable<ModuleInfoExtended> Sort(IEnumerable<ModuleInfoExtended> source)
        {
            var orderedModules = source
                .OrderByDescending(x => x.IsOfficial)
                .ThenBy(x => x.Id, new AlphanumComparatorFast())
                .ToArray();

            return ModuleSorter.TopologySort(orderedModules, module => ModuleUtilities.GetDependencies(orderedModules, module));
        }

        var sorted = Sort(modules.Select(x => x.ModuleInfoExtended)).Select((x, i) => new { Item = x.Id, Index = i }).ToDictionary(x => x.Item, x => x.Index);
        modules.Sort(new ByIndexComparer<IModuleViewModel>(x => sorted.TryGetValue(x.ModuleInfoExtended.Id, out var idx) ? idx : -1));
    }
}