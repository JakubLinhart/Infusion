using System.Linq;
using System.Text;
using System.IO;
using System.Collections.Generic;

public static class Phantoms
{
    private static List<Phantom> phantoms = new List<Phantom>(); 

    public static EventJournal journal = UO.CreateEventJournal();

    public static ModelId WalkableType { get; set; } = 0x0E21;
    public static ModelId BlockingType { get; set; } = 0x1368;
    
    private const int previousLocationsCapacity = 32;
    private static Location3D? locationBeforeJump;
    private static Queue<Location3D> previousLocations = new Queue<Location3D>(previousLocationsCapacity);
    
    public static ModelId CurrentType
    {
        get => currentType;
        set
        {
            currentType = value;
            foreach (var phantom in phantoms)
            {
                phantom.Type = currentType;
            }
        }
    }
    
    private static ModelId currentType = BlockingType;
    private static string currentFileName;
    
    public static void Load(string fileName)
    {
        currentFileName = fileName;

        foreach (var line in File.ReadAllLines(fileName))
        {
            var location = ParseLocation(line);
                
            AddPhantom(location);
        }
    }
    
    private static Location3D ParseLocation(string locationString)
    {
        var coordinates = locationString.Split(',');
        return new Location3D(
            ushort.Parse(coordinates[0].Trim()),
            ushort.Parse(coordinates[1].Trim()),
            byte.Parse(coordinates[2].Trim()));
    }
    
    public static void Save(string fileName)
    {
        currentFileName = fileName;
        Save();
    }
    
    private static void Save()
    {
        if (string.IsNullOrEmpty(currentFileName))
        {
            UO.Log("No file name for phantoms specified");
            return;
        }
    
        var content = new StringBuilder();
    
        foreach (var phantom in phantoms)
        {
            content.AppendLine(phantom.Location.ToString());
        }
        
        File.WriteAllText(currentFileName, content.ToString());
    }

    public static void Run()
    {
        journal.When<PlayerLocationChangedEvent>(HandlePlayerMove)
            .Incomming();
    }
    
    public static void Enable()
    {
        UO.CommandHandler.Invoke(",phantoms");
    }
    
    public static void Disable()
    {
        UO.CommandHandler.Terminate("phantoms");
    }
    
    public static void Toggle()
    {
        if (UO.CommandHandler.IsCommandRunning("phantoms"))
        {
            UO.Log("Disabling phantoms");
            Disable();
        }
        else
        {
            UO.Log("Enabling phantoms");        
            Enable();
        }
    }
    
    public static void AddPhantomAtCurrentPosition()
    {
        AddPhantomAndSave(UO.Me.Location);
    }
    
    public static void LocationDump()
    {
        foreach (var location in previousLocations)
        {
            UO.Log(location.ToString());
        }
    }
    
    public static void AddPhantomAtLocationCommand(string parameters)
    {
        var location = ParseLocation(parameters);
        AddPhantomAndSave(location);
    }
    
    public static void AddPhantomAtLastJump()
    {
        if (locationBeforeJump.HasValue)
        {
            AddPhantomAndSave(locationBeforeJump.Value);
        }
        else
            UO.Log("No previous jump");
    }
    
    public static void AddPhantomCommand()
    {
        var targetInfo = UO.Info();
        if (targetInfo.HasValue)
        {
            AddPhantom(targetInfo.Value.Location);
        }
    }
    
    public static void RefreshAll()
    {
        foreach (var phantom in phantoms)
        {
            phantom.UpdateVisibility(UO.Me.Location);
        }
    }

    public static void AddPhantomAndSave(Location3D location)
    {
        AddPhantom(location);
        Save();
        
        UO.Log($"Phantom added at {location}");
    }

    public static void AddPhantom(Location3D location)
    {
        var phantom = new Phantom(location, currentType);
        phantoms.Add(phantom);
        phantom.UpdateVisibility(UO.Me.Location);
    }

    public static void RemovePhantomCommand()
    {
        var targetInfo = UO.Info();
        if (!targetInfo.HasValue)
        {
            UO.Log("targeting cancelled");
            return;
        }
        
        var phantomsToRemove = (targetInfo?.Id != null) ?
            phantoms.Where(x => x.Id == targetInfo.Value.Id) :
            phantoms.Where(x => (Location2D)x.Location == (Location2D)targetInfo.Value.Location);

        foreach (var phantom in phantomsToRemove.ToArray())
        {
            phantom.Remove();
            phantoms.Remove(phantom);
        }

        Save();
    }    
    
    private static void HandlePlayerMove(PlayerLocationChangedEvent ev)
    {
        if (previousLocations.Count >= previousLocationsCapacity - 1)
            previousLocations.Dequeue();
    
        previousLocations.Enqueue(ev.NewLocation);
        
        if (ev.NewLocation.GetDistance(ev.OldLocation) > 3)
        {
            locationBeforeJump = ev.OldLocation.LocationInDirection(UO.Me.Direction);
            UO.Log($"Jump {locationBeforeJump} -> {ev.NewLocation}");
        }
    
        foreach (var phantom in phantoms)
        {
            phantom.UpdateVisibility(ev.NewLocation);
        }
    }
    
    private static ObjectId nextPhantomId = 0x4FFFFFFF;
    
    private static ObjectId NextPhantomId() => nextPhantomId--;

    private class Phantom
    {
        public Location3D Location { get; }
        public ObjectId? Id { get; set; }
        private ModelId type;
        public ModelId Type
        {
            get => this.type;
            set
            {
                if (value != type)
                {
                   type = value;
                   Show();
                }
            }
        }
        
        public Phantom(Location3D location, ModelId type)
        {
            this.type = type;
            Location = location;
        }
        
        public void UpdateVisibility(Location3D viewCenter)
        {
            if (viewCenter.GetDistance(Location) < 23)
            {
                Show();
            }
            
            if (Id.HasValue && viewCenter.GetDistance(Location) >= 23)
            {
                Remove();
            }
        }
        
        public void Remove()
        {
            if (Id.HasValue)
            {
                UO.Client.DeleteItem(Id.Value);
                Id = null;
            }
        }

        
        private void Show()
        {
            if (!Id.HasValue)
            {
                Id = NextPhantomId();
            }
            else
            {
                UO.Client.DeleteItem(Id.Value);
            }
        
            UO.Client.CreatePhantom(Id.Value, Type, Location);
        }
    }    
}

UO.RegisterBackgroundCommand("phantoms", Phantoms.Run);
UO.RegisterCommand("phantoms-enable", Phantoms.Enable);
UO.RegisterCommand("phantoms-disable", Phantoms.Disable);
UO.RegisterCommand("phantoms-toggle", Phantoms.Toggle);

UO.RegisterCommand("phantoms-locdump", Phantoms.LocationDump);
UO.RegisterCommand("phantoms-addcurrent", Phantoms.AddPhantomAtCurrentPosition);
UO.RegisterCommand("phantoms-addloc", Phantoms.AddPhantomAtLocationCommand);
UO.RegisterCommand("phantoms-addjump", Phantoms.AddPhantomAtLastJump);
UO.RegisterCommand("phantoms-save", fileName => Phantoms.Save(fileName));
UO.RegisterCommand("phantoms-load", fileName => Phantoms.Load(fileName));
UO.RegisterCommand("phantoms-refresh", Phantoms.RefreshAll);
UO.RegisterCommand("phantoms-blocking", () => Phantoms.CurrentType = Phantoms.BlockingType);
UO.RegisterCommand("phantoms-walkable", () => Phantoms.CurrentType = Phantoms.WalkableType);
UO.RegisterCommand("phantoms-add", Phantoms.AddPhantomCommand);
UO.RegisterCommand("phantoms-remove", Phantoms.RemovePhantomCommand);
