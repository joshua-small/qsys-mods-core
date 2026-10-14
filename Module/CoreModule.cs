using QsysMods.Core;

namespace Designer.Modules.ModsCore;
public sealed class CoreModule : ModModule
{
    public CoreModule() => Mods.Register("mods-core", _ => { });
}
