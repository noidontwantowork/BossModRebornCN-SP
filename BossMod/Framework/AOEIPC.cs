using BossMod.Components;
using System.IO;
using System.Reflection;
using System.Text.Json;

namespace BossMod;

// exposes currently visible aoe zones to other plugins via the 'AOE.Active' IPC endpoint
public static class AOEIPC
{
    private static int _lastSentCount = -1;
    private static string? _lastSentSig;

    private static readonly FieldInfo? SharedSrcField = typeof(GenericSharedTankbuster).GetField("Source", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? SharedTgtField = typeof(GenericSharedTankbuster).GetField("Target", BindingFlags.Instance | BindingFlags.NonPublic);

    private static string? _debugLogPath;
    private static long _debugLogSize;

    private static string DebugLogPath
    {
        get
        {
            if (_debugLogPath == null)
            {
                try
                {
                    _debugLogPath = Path.Combine(Service.PluginInterface.ConfigDirectory.FullName, "AOEIPCDebug.log");
                }
                catch
                {
                    _debugLogPath = Path.Combine(Path.GetTempPath(), "AOEIPCDebug.log");
                }
            }
            return _debugLogPath;
        }
    }

    public static string CollectActiveAOEs(BossModuleManager bossmod)
    {
        var module = bossmod.ActiveModule;
        if (module == null)
        {
            return "[]";
        }

        var result = new List<AOEIPCDto>(16);
        var player = Service.ObjectTable.LocalPlayer;
        var defaultY = player?.Position.Y ?? 0f;

        // zones explicitly drawn by the active module this frame
        foreach (var zone in MiniArena.DrawnZones)
        {
            var dto = ConvertZone(zone, defaultY);
            if (dto != null)
            {
                result.Add(dto);
            }
        }

        var components = module.Components;
        var count = components.Count;
        var batch = 0;

        // stacks & spreads
        for (var i = 0; i < count; ++i)
        {
            ++batch;
            if (components[i] is not GenericStackSpread stackSpread)
            {
                continue;
            }
            foreach (var stack in stackSpread.Stacks)
            {
                if (stackSpread.IncludeDeadTargets || !stack.Target.IsDead)
                {
                    result.Add(new AOEIPCDto
                    {
                        ShapeType = (int)AOEIPCShapeType.Stack,
                        OriginX = stack.Target.Position.X,
                        OriginZ = stack.Target.Position.Z,
                        OriginY = defaultY,
                        Rotation = 0f,
                        P1 = stack.Radius,
                        IsDanger = true,
                        Batch = batch,
                    });
                }
            }
            foreach (var spread in stackSpread.Spreads)
            {
                if (stackSpread.IncludeDeadTargets || !spread.Target.IsDead)
                {
                    result.Add(new AOEIPCDto
                    {
                        ShapeType = (int)AOEIPCShapeType.Spread,
                        OriginX = spread.Target.Position.X,
                        OriginZ = spread.Target.Position.Z,
                        OriginY = defaultY,
                        Rotation = 0f,
                        P1 = spread.Radius,
                        IsDanger = true,
                        Batch = batch,
                    });
                }
            }
        }

        // baits: stacks are reported as stack markers, everything else as its own shape
        batch = 0;
        for (var i = 0; i < count; ++i)
        {
            ++batch;
            if (components[i] is not GenericBaitAway baitAway)
            {
                continue;
            }
            var isStack = baitAway is GenericBaitStack;
            foreach (var bait in baitAway.ActiveBaits)
            {
                var origin = (baitAway.CenterAtTarget ? bait.Target : bait.Source).Position + bait.Offset;
                if (isStack)
                {
                    switch (bait.Shape)
                    {
                        case AOEShapeCircle circle:
                            result.Add(new AOEIPCDto
                            {
                                ShapeType = (int)AOEIPCShapeType.Stack,
                                OriginX = origin.X,
                                OriginZ = origin.Z,
                                OriginY = defaultY,
                                P1 = circle.Radius,
                                IsDanger = true,
                                Batch = batch,
                            });
                            break;
                        case AOEShapeRect rect:
                            result.Add(new AOEIPCDto
                            {
                                ShapeType = (int)AOEIPCShapeType.FriendlyRect,
                                OriginX = origin.X,
                                OriginZ = origin.Z,
                                OriginY = defaultY,
                                Rotation = (bait.Rotation + rect.DirectionOffset).Rad,
                                P1 = rect.LengthFront,
                                P2 = rect.HalfWidth,
                                P3 = rect.LengthBack,
                                IsDanger = true,
                                Batch = batch,
                            });
                            break;
                        default:
                            AddConvertedShape(result, bait.Shape, origin, bait.Rotation, defaultY, batch);
                            break;
                    }
                }
                else
                {
                    AddConvertedShape(result, bait.Shape, origin, bait.Rotation, defaultY, batch);
                }
            }
        }

        // shared tankbusters (source/target are protected, so they are read through reflection)
        batch = 0;
        for (var i = 0; i < count; ++i)
        {
            ++batch;
            if (components[i] is not GenericSharedTankbuster tankbuster)
            {
                continue;
            }
            if (SharedSrcField?.GetValue(tankbuster) is not Actor src || SharedTgtField?.GetValue(tankbuster) is not Actor tgt)
            {
                continue;
            }
            var origin = tankbuster.OriginAtTarget ? tgt.Position : src.Position;
            var rotation = tankbuster.OriginAtTarget ? default : Angle.FromDirection(tgt.Position - src.Position);
            var dto = ConvertShape(tankbuster.Shape, origin, rotation, defaultY);
            if (dto != null)
            {
                dto.Batch = batch;
                if (!AlreadyInDrawnZones(dto))
                {
                    result.Add(dto);
                }
            }
        }

        // knockback pushes of the local player, reported as directional vectors
        var localPlayer = module.WorldState.Party.Player();
        if (localPlayer != null)
        {
            batch = 0;
            for (var i = 0; i < count; ++i)
            {
                ++batch;
                if (components[i] is not GenericKnockback knockback)
                {
                    continue;
                }
                var movements = knockback.CalculateMovements(0, localPlayer);
                var movesCount = movements.Count;
                for (var j = 0; j < movesCount; ++j)
                {
                    var (from, to) = movements[j];
                    var dir = to - from;
                    var len = dir.Length();
                    if (len < 0.1f)
                    {
                        continue;
                    }
                    result.Add(new AOEIPCDto
                    {
                        ShapeType = (int)AOEIPCShapeType.Knockback,
                        OriginX = from.X,
                        OriginZ = from.Z,
                        OriginY = defaultY,
                        Rotation = Angle.FromDirection(dir).Rad,
                        P1 = len,
                        Batch = batch,
                    });
                }
            }
        }

        var sig = string.Join(",", result.Select(d => $"{(AOEIPCShapeType)(byte)d.ShapeType}@({d.OriginX:0},{d.OriginZ:0}){(d.IsDanger ? "*" : "")}"));
        if (result.Count != _lastSentCount || sig != _lastSentSig)
        {
            DebugLog($"[BossMod] zones {_lastSentCount} -> {result.Count} [{sig}]");
            _lastSentCount = result.Count;
            _lastSentSig = sig;
        }

        return JsonSerializer.Serialize(result);

        void AddConvertedShape(List<AOEIPCDto> list, AOEShape shape, WPos origin, Angle rotation, float y, int zoneBatch)
        {
            var dto = ConvertShape(shape, origin, rotation, y);
            if (dto != null)
            {
                dto.Batch = zoneBatch;
                if (!AlreadyInDrawnZones(dto))
                {
                    list.Add(dto);
                }
            }
        }

        bool AlreadyInDrawnZones(AOEIPCDto dto) => MiniArena.DrawnZones.Any(z =>
            z.Batch == dto.Batch && z.Shape == dto.ShapeType
            && MathF.Abs(z.Origin.X - dto.OriginX) < 0.01f && MathF.Abs(z.Origin.Z - dto.OriginZ) < 0.01f
            && MathF.Abs(z.P1 - dto.P1) < 0.01f && MathF.Abs(z.P2 - dto.P2) < 0.01f);
    }

    private static AOEIPCDto? ConvertZone(in MiniArena.DrawnZone zone, float defaultY)
    {
        var dto = new AOEIPCDto
        {
            ShapeType = zone.Shape,
            OriginX = zone.Origin.X,
            OriginZ = zone.Origin.Z,
            OriginY = defaultY,
            Rotation = zone.Rotation.Rad,
            IsDanger = zone.IsDanger,
            IsFriendly = zone.IsFriendly,
            Batch = zone.Batch,
        };
        switch ((AOEIPCShapeType)(byte)zone.Shape)
        {
            case AOEIPCShapeType.Circle:
                dto.P1 = zone.P1;
                break;
            case AOEIPCShapeType.Cone:
                // RecordZone stores (innerRadius, outerRadius, halfAngle)
                dto.P1 = zone.P2;
                dto.P2 = zone.P3;
                break;
            case AOEIPCShapeType.Donut:
                dto.P1 = zone.P1;
                dto.P2 = zone.P2;
                break;
            case AOEIPCShapeType.DonutSector:
                dto.P1 = zone.P1;
                dto.P2 = zone.P2;
                dto.P3 = zone.P3;
                break;
            case AOEIPCShapeType.Rect:
                // RecordZone stores (lenFront, lenBack, halfWidth)
                dto.P1 = zone.P1;
                dto.P2 = zone.P3;
                dto.P3 = zone.P2;
                break;
            case AOEIPCShapeType.Cross:
                dto.P1 = zone.P1;
                dto.P2 = zone.P2;
                break;
            case AOEIPCShapeType.TriCone:
                dto.P1 = zone.P1;
                dto.P2 = zone.P2;
                break;
            case AOEIPCShapeType.Capsule:
                dto.P1 = zone.P1;
                dto.P2 = zone.P2;
                break;
            default:
                return null;
        }
        return dto;
    }

    private static AOEIPCDto? ConvertShape(AOEShape shape, WPos origin, Angle rotation, float defaultY)
    {
        var dto = new AOEIPCDto
        {
            OriginX = origin.X,
            OriginZ = origin.Z,
            OriginY = defaultY,
            IsDanger = true,
        };
        switch (shape)
        {
            case AOEShapeCircle circle:
                dto.ShapeType = (int)AOEIPCShapeType.Circle;
                dto.P1 = circle.Radius;
                break;
            case AOEShapeCone cone:
                dto.ShapeType = (int)AOEIPCShapeType.Cone;
                dto.P1 = cone.Radius;
                dto.P2 = cone.HalfAngle.Rad;
                dto.Rotation = (rotation + cone.DirectionOffset).Rad;
                break;
            case AOEShapeDonutSector sector:
                dto.ShapeType = (int)AOEIPCShapeType.DonutSector;
                dto.P1 = sector.InnerRadius;
                dto.P2 = sector.OuterRadius;
                dto.P3 = sector.HalfAngle.Rad;
                dto.Rotation = (rotation + sector.DirectionOffset).Rad;
                break;
            case AOEShapeDonut donut:
                dto.ShapeType = (int)AOEIPCShapeType.Donut;
                dto.P1 = donut.InnerRadius;
                dto.P2 = donut.OuterRadius;
                break;
            case AOEShapeRect rect:
                dto.ShapeType = (int)AOEIPCShapeType.Rect;
                dto.P1 = rect.LengthFront;
                dto.P2 = rect.HalfWidth;
                dto.P3 = rect.LengthBack;
                dto.Rotation = (rotation + rect.DirectionOffset).Rad;
                break;
            case AOEShapeCross cross:
                dto.ShapeType = (int)AOEIPCShapeType.Cross;
                dto.P1 = cross.Length;
                dto.P2 = cross.HalfWidth;
                dto.Rotation = (rotation + cross.DirectionOffset).Rad;
                break;
            case AOEShapeTriCone triCone:
                dto.ShapeType = (int)AOEIPCShapeType.TriCone;
                dto.P1 = triCone.SideLength;
                dto.P2 = triCone.HalfAngle.Rad;
                dto.Rotation = (rotation + triCone.DirectionOffset).Rad;
                break;
            case AOEShapeCapsule capsule:
                dto.ShapeType = (int)AOEIPCShapeType.Capsule;
                dto.P1 = capsule.Radius;
                dto.P2 = capsule.Length;
                dto.Rotation = (rotation + capsule.DirectionOffset).Rad;
                break;
            default:
                return null;
        }
        return dto;
    }

    private static void DebugLog(string msg)
    {
        try
        {
            var path = DebugLogPath;
            _debugLogSize += msg.Length + 2;
            if (_debugLogSize > 5242880)
            {
                File.WriteAllText(path, "");
                _debugLogSize = 0;
            }
            File.AppendAllText(path, $"{DateTime.Now:HH:mm:ss.fff} {msg}\n");
        }
        catch
        {
            // debug logging must never break the ipc endpoint
        }
    }
}

public enum AOEIPCShapeType : byte
{
    Circle,
    Cone,
    Rect,
    Donut,
    DonutSector,
    Cross,
    TriCone,
    Capsule,
    Custom,
    Stack,
    Spread,
    Tower,
    FriendlyRect,
    Knockback,
}

public sealed class AOEIPCDto
{
    public int ShapeType { get; set; }
    public float P1 { get; set; }
    public float P2 { get; set; }
    public float P3 { get; set; }
    public float OriginX { get; set; }
    public float OriginZ { get; set; }
    public float OriginY { get; set; }
    public float Rotation { get; set; }
    public bool IsDanger { get; set; }
    public bool IsFriendly { get; set; }
    public int Batch { get; set; }
}
