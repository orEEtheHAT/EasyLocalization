using EasyBuilding;
using EasyInteraction;
using EasyResources;
using UnityEngine;

namespace EasyLocalization.Integrations
{
    /// <summary>Optional adapter. Reuses the same public IInteractionTextProvider contract as EasyInteraction's Localization sample.</summary>
    [RequireComponent(typeof(LocalizedCatalogCache))]
    [AddComponentMenu("EasyLocalization/Integrations/Gameplay Translations")]
    public sealed class GameplayTranslations : LocalizedCatalogAdapter, IInteractionTextProvider, IBuildingMessageResolver
    {
        [Tooltip("Optional. Refreshes the visible prompt as soon as translated text is ready.")]
        public InteractionPresenter presenter;

        protected override void OnTranslationsChanged()
        {
            base.OnTranslationsChanged();
            if (this && isActiveAndEnabled && presenter) presenter.Refresh();
        }
        public string ResourceName(ResourceDefinition definition) => definition ? Translate("resource." + definition.Id, definition.DisplayName) : "";
        public string BuildingName(BuildingDefinition definition) => definition ? Translate("building." + definition.Id, definition.Name) : "";
        public string Resolve(in PlacementResult result) => Translate("building.result." + result.Failure, result.Reason);
    }
}
