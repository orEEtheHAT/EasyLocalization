using EasyResources;
using UnityEngine;

namespace EasyLocalization.Integrations
{
    [AddComponentMenu("EasyLocalization/Integrations/Resource Names")]
    public sealed class ResourceNames : LocalizedCatalogAdapter
    {
        public string GetName(ResourceDefinition definition) => definition
            ? Translate("resource." + definition.Id, definition.DisplayName) : "";
    }
}
