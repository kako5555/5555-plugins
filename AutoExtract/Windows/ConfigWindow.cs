using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace AutoExtract.Windows;

public class ConfigWindow : Window, IDisposable
{
    private readonly Configuration configuration;

    public ConfigWindow(Plugin plugin) : base("AutoExtract Configuration###AutoExtractConfig")
    {
        Flags = ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar |
                ImGuiWindowFlags.NoScrollWithMouse;

        Size = new Vector2(320, 150);
        SizeCondition = ImGuiCond.Always;

        configuration = plugin.Configuration;
    }

    public void Dispose() { }

    public override void PreDraw()
    {
        if (configuration.IsConfigWindowMovable)
        {
            Flags &= ~ImGuiWindowFlags.NoMove;
        }
        else
        {
            Flags |= ImGuiWindowFlags.NoMove;
        }
    }

    public override void Draw()
    {
        ImGui.Text("AutoExtract Settings");
        ImGui.Separator();

        var autoExtractEnabled = configuration.AutoExtractEnabled;
        if (ImGui.Checkbox("Enable Auto Extract Materia", ref autoExtractEnabled))
        {
            configuration.AutoExtractEnabled = autoExtractEnabled;
            configuration.Save();
        }

        ImGui.TextWrapped("When enabled, this will automatically extract materia from equipment with 100%% spiritbond when you open the Materialize window.");

        ImGui.Separator();

        var movable = configuration.IsConfigWindowMovable;
        if (ImGui.Checkbox("Movable Config Window", ref movable))
        {
            configuration.IsConfigWindowMovable = movable;
            configuration.Save();
        }
    }
}