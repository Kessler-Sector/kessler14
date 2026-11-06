using Content.Shared.CartridgeLoader;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._NF.BountyContracts;

[Serializable, NetSerializable]
public enum BountyContractCategory : byte
{
    Announcement,
    Criminal,
    Buy,
    Sell,
    Barter,
    Vacancy,
    JobSeeker,
    Construction,
    Service,
    Advertisement,
    Social,
    Other
}

[Serializable, NetSerializable]
public struct BountyContractCategoryMeta
{
    public string Name = "";
    public Color UiColor = Color.FromHex("#3c3c3c");
    public LocId? Announcement = null;
    public bool? TargetIsPoster = true;
    public bool? ShowVessel = true;
    public bool? DefaultCustomVessel = false;
    public bool? ShowReward = true;
    public bool? ShowTitle = true;
    public string TitleLabel = "bounty-contracts-ui-create-title";
    public string TitlePlaceholder = "bounty-contracts-ui-create-title-placeholder";
    public bool? ShowDNA = false;

    public BountyContractCategoryMeta()
    {
    }
}

[NetSerializable, Serializable]
public struct BountyContractTargetInfo
{
    public string Name;
    public string? DNA;

    public bool Equals(BountyContractTargetInfo other)
    {
        return DNA == other.DNA;
    }

    public override bool Equals(object? obj)
    {
        return obj is BountyContractTargetInfo other && Equals(other);
    }

    public override int GetHashCode()
    {
        return DNA != null ? DNA.GetHashCode() : 0;
    }
}

[NetSerializable, Serializable]
public struct BountyContractRequest
{
    public ProtoId<BountyContractCollectionPrototype> Collection;
    public BountyContractCategory Category;
    public string Name;
    public string Contact;
    public string? DNA;
    public string Vessel;
    public int Reward;
    public string? Title;
    public string Description;
}

// Kessler: a data definition, so posted contracts are saved with the sector. It held readonly fields that couldn't be
// written to a save at all.
[NetSerializable, Serializable, DataDefinition]
public sealed partial class BountyContract
{
    [DataField] public uint ContractId;
    [DataField] public BountyContractCategory Category;
    [DataField] public string Name = string.Empty;
    [DataField] public int Reward;
    [DataField] public NetEntity AuthorUid;
    [DataField] public string? DNA;
    [DataField] public string? Vessel;
    [DataField] public string? Description;
    [DataField] public string? Title;
    [DataField] public string? Contact;
    [DataField] public string? Author;
    [DataField] public DateTime Created;
    public bool AuthorIsActive = false; // Worked out each time the list is sent.

    // Kessler: for loading.
    public BountyContract()
    {
    }

    public BountyContract(uint contractId, BountyContractCategory category, string name,
        int reward, NetEntity authorUid, string? dna, string? vessel, string? description, string? author, string? title, string? contact, DateTime created)
    {
        ContractId = contractId;
        Category = category;
        Name = name;
        Reward = reward;
        AuthorUid = authorUid;
        DNA = dna;
        Vessel = vessel;
        Description = description;
        Author = author;
        Title = title;
        Contact = contact;
        Created = created;
    }
}

[NetSerializable, Serializable]
public sealed class BountyContractCreateUiState : BoundUserInterfaceState
{
    public readonly ProtoId<BountyContractCollectionPrototype> Collection;
    public readonly List<BountyContractTargetInfo> Targets;
    public readonly List<string> Vessels;

    public BountyContractCreateUiState(
        ProtoId<BountyContractCollectionPrototype> collection,
        List<BountyContractTargetInfo> targets,
        List<string> vessels)
    {
        Collection = collection;
        Targets = targets;
        Vessels = vessels;
    }
}

[NetSerializable, Serializable]
public sealed class BountyContractListUiState(ProtoId<BountyContractCollectionPrototype> collection,
        List<ProtoId<BountyContractCollectionPrototype>> collections,
        List<BountyContract> contracts,
        bool isAllowedCreateBounties,
        bool isAllowedRemoveBounties,
        NetEntity authorUid,
        bool notificationsEnabled,
        Dictionary<ProtoId<BountyContractCollectionPrototype>, int> contractCounts) : BoundUserInterfaceState
{
    public readonly ProtoId<BountyContractCollectionPrototype> Collection = collection;
    public readonly List<ProtoId<BountyContractCollectionPrototype>> Collections = collections;
    public readonly List<BountyContract> Contracts = contracts;
    public readonly Dictionary<ProtoId<BountyContractCollectionPrototype>, int> ContractCounts = contractCounts;
    public readonly bool IsAllowedCreateBounties = isAllowedCreateBounties;
    public readonly bool IsAllowedRemoveBounties = isAllowedRemoveBounties;
    public readonly NetEntity AuthorUid = authorUid;
    public readonly bool NotificationsEnabled = notificationsEnabled;
}

public enum BountyContractCommand : byte
{
    OpenCreateUi = 0,
    CloseCreateUi = 1,
    RefreshList = 2,
    ToggleNotifications = 3,
}

[NetSerializable, Serializable]
public sealed class BountyContractCommandMessageEvent(BountyContractCommand command, ProtoId<BountyContractCollectionPrototype> collection) : CartridgeMessageEvent
{
    public readonly ProtoId<BountyContractCollectionPrototype> Collection = collection;
    public readonly BountyContractCommand Command = command;
}

[NetSerializable, Serializable]
public sealed class BountyContractTryRemoveMessageEvent(uint contractId) : CartridgeMessageEvent
{
    public readonly uint ContractId = contractId;
}

[NetSerializable, Serializable]
public sealed class BountyContractTryCreateMessageEvent(BountyContractRequest contract) : CartridgeMessageEvent
{
    public readonly BountyContractRequest Contract = contract;
}

public abstract class SharedBountyContractSystem : EntitySystem
{
    public const int MaxNameLength = 32;
    public const int MaxContactLength = 32;
    public const int MaxVesselLength = 32;
    public const int MaxTitleLength = 60;
    public const int MaxDescriptionLength = 400;
    public const int DefaultReward = 5000;

    // TODO: move this to prototypes?
    public static readonly Dictionary<BountyContractCategory, BountyContractCategoryMeta> CategoriesMeta = new()
    {
        [BountyContractCategory.Announcement] = new BountyContractCategoryMeta
        {
            Name = "bounty-contracts-category-announcement",
            UiColor = Color.FromHex("#520c52"),
            Announcement = "bounty-contracts-announcement-command-create",
            ShowVessel = false,
            ShowTitle = true,
            ShowReward = false
        },
        [BountyContractCategory.Criminal] = new BountyContractCategoryMeta
        {
            Name = "bounty-contracts-category-criminal",
            UiColor = Color.FromHex("#520c0c"),
            Announcement = "bounty-contracts-announcement-criminal-create",
            ShowDNA = true,
        },
        [BountyContractCategory.Buy] = new BountyContractCategoryMeta
        {
            Name = "bounty-contracts-category-buy",
            UiColor = Color.FromHex("#320c0c"),
            Announcement = "bounty-contracts-announcement-buy-create",
            ShowTitle = true,
            TitleLabel = "bounty-contracts-ui-create-title-item",
            TitlePlaceholder = "bounty-contracts-ui-create-item-placeholder"
        },
        [BountyContractCategory.Sell] = new BountyContractCategoryMeta
        {
            Name = "bounty-contracts-category-sell",
            UiColor = Color.FromHex("#0c0c32"),
            Announcement = "bounty-contracts-announcement-sell-create",
            ShowTitle = true,
            TitleLabel = "bounty-contracts-ui-create-title-item",
            TitlePlaceholder = "bounty-contracts-ui-create-item-placeholder"
        },
        [BountyContractCategory.Barter] = new BountyContractCategoryMeta
        {
            Name = "bounty-contracts-category-barter",
            UiColor = Color.FromHex("#320c32"),
            Announcement = "bounty-contracts-announcement-barter-create",
            ShowTitle = true,
            ShowReward = false
        },
        [BountyContractCategory.Vacancy] = new BountyContractCategoryMeta
        {
            Name = "bounty-contracts-category-vacancy",
            UiColor = Color.FromHex("#0c3866"),
            Announcement = "bounty-contracts-announcement-vacancy-create",
        },
        [BountyContractCategory.JobSeeker] = new BountyContractCategoryMeta
        {
            Name = "bounty-contracts-category-job",
            UiColor = Color.FromHex("#0c6638"),
            Announcement = "bounty-contracts-announcement-job-create",
            ShowVessel = false
        },
        [BountyContractCategory.Construction] = new BountyContractCategoryMeta
        {
            Name = "bounty-contracts-category-construction",
            UiColor = Color.FromHex("#664a06"),
            Announcement = "bounty-contracts-announcement-construction-create",
            ShowTitle = true,
        },
        [BountyContractCategory.Service] = new BountyContractCategoryMeta
        {
            Name = "bounty-contracts-category-service",
            UiColor = Color.FromHex("#01551e"),
            Announcement = "bounty-contracts-announcement-service-create",
            ShowTitle = true,
        },
        [BountyContractCategory.Advertisement] = new BountyContractCategoryMeta
        {
            Name = "bounty-contracts-category-advert",
            UiColor = Color.FromHex("#553333"),
            Announcement = "bounty-contracts-announcement-advert-create",
            ShowTitle = true,
            ShowReward = false
        },
        [BountyContractCategory.Social] = new BountyContractCategoryMeta
        {
            Name = "bounty-contracts-category-social",
            UiColor = Color.FromHex("#553c3c"),
            Announcement = "bounty-contracts-announcement-social-create",
            ShowTitle = true,
            ShowReward = false
        },
        [BountyContractCategory.Other] = new BountyContractCategoryMeta
        {
            Name = "bounty-contracts-category-other",
            UiColor = Color.FromHex("#3c3c3c"),
            Announcement = "bounty-contracts-announcement-generic-create",
            ShowTitle = true
        },
    };
}
