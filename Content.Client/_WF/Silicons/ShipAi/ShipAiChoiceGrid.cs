using System.Numerics;
using Content.Shared.Silicons.StationAi;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;
using Content.Shared._WF.Silicons.ShipAi;

namespace Content.Client._WF.Silicons.ShipAi;

public sealed class ShipAiChoiceGrid : GridContainer
{
    private const float IconScale = 2f;
    private const string NoneId = "";
    private const int SingleLineLength = 11;

    public event Action<string?>? OnSelected;

    public ShipAiChoiceGrid(IPrototypeManager protoManager, ProtoId<StationAiCustomizationGroupPrototype> groupId, string? selected, string? noneLabel)
    {
        Columns = 5;
        HorizontalExpand = true;

        var group = new ButtonGroup();
        if (noneLabel != null)
            AddChoice(group, NoneId, noneLabel, null, selected == null);

        if (!protoManager.TryIndex(groupId, out var choices))
            return;

        foreach (var choiceId in choices.ProtoIds)
        {
            if (!ShipAiSystem.ShowsChoice(true, groupId, choiceId))
                continue;

            if (!protoManager.TryIndex(choiceId, out var choice))
                continue;

            AddChoice(group, choiceId, Loc.GetString(choice.Name), choice, selected == choiceId.Id);
        }
    }

    private void AddChoice(ButtonGroup group, string id, string name, StationAiCustomizationPrototype? choice, bool pressed)
    {
        var button = new ContainerButton
        {
            StyleClasses = { ContainerButton.StyleClassButton },
            ToggleMode = true,
            Group = group,
            Pressed = pressed,
            HorizontalExpand = true,
            ToolTip = name,
        };

        var box = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalAlignment = HAlignment.Center,
        };

        var preview = new AnimatedTextureRect
        {
            HorizontalAlignment = HAlignment.Center,
            SetWidth = 32 * IconScale,
            SetHeight = 32 * IconScale,
        };
        preview.DisplayRect.TextureScale = new Vector2(IconScale, IconScale);

        if (choice?.PreviewBackground != null)
            preview.SetFromSpriteSpecifier(choice.PreviewBackground);

        if (choice != null && choice.LayerData.TryGetValue(choice.PreviewKey, out var layer) && layer.RsiPath != null && layer.State != null)
        {
            var icon = new AnimatedTextureRect
            {
                HorizontalAlignment = HAlignment.Center,
                VerticalAlignment = VAlignment.Center,
                SetWidth = 32 * IconScale,
                SetHeight = 32 * IconScale,
            };
            icon.DisplayRect.TextureScale = new Vector2(IconScale, IconScale);
            icon.SetFromSpriteSpecifier(new SpriteSpecifier.Rsi(new ResPath(layer.RsiPath), layer.State));
            preview.AddChild(icon);
        }

        box.AddChild(preview);
        box.AddChild(new Label
        {
            Text = SplitName(name),
            Align = Label.AlignMode.Center,
            HorizontalAlignment = HAlignment.Center,
        });
        button.AddChild(box);
        button.OnPressed += _ => OnSelected?.Invoke(id == NoneId ? null : id);
        AddChild(button);
    }

    private static string SplitName(string name)
    {
        if (name.Length <= SingleLineLength)
            return name;

        var middle = name.Length / 2;
        var split = -1;
        for (var i = 0; i < name.Length; i++)
        {
            if (name[i] == ' ' && (split == -1 || Math.Abs(i - middle) < Math.Abs(split - middle)))
                split = i;
        }

        return split == -1 ? name : name[..split] + "\n" + name[(split + 1)..];
    }
}
