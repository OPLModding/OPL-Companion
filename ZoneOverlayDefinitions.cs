#nullable disable

using System;
using System.Collections.Generic;
using UnityEngine;

namespace OPLServerBanners;

internal static class ZoneOverlayDefinitions
{
    public const float MinX = -23.099999f;
    public const float MaxX = 23.099999f;
    public const float MinZ = -46.349995f;
    public const float MaxZ = 46.349993f;

    private const float GoalLineZ = 39.85f;
    private const float CircleSideEdgeZ = 22.9f;
    private const float SlotPointZ = 27.0f;
    private const float AboveCirclesCenterEdgeZ = 13.15f;
    private const float BoardX = 23.1f;
    private const float ZoneAlpha = 0.6f;
    private const float BoardVisualInset = 0.12f;
    private const float Z6LabelBackBoardShift = 1.6f;
    private const float StraightSideBoardX = 22.438606f;
    private const float CornerTangentZ = 34.249712f;
    private const float CenterSideBoardExtraX = 0.1f;
    private const float BackBoardHalfWidth = 10.99972f;
    private const float BackBoardZ = 45.6886f;

    private static readonly Vector2[] BoardHull =
    {
        new(-23.099999f, 0.0f),
        new(-22.438606f, -34.249713f),
        new(-22.255013f, -35.487397f),
        new(-21.950991f, -36.701123f),
        new(-21.529465f, -37.87921f),
        new(-20.994498f, -39.010304f),
        new(-20.35124f, -40.083516f),
        new(-19.605884f, -41.088508f),
        new(-18.765612f, -42.015605f),
        new(-17.838516f, -42.855879f),
        new(-16.833522f, -43.601233f),
        new(-15.760309f, -44.244491f),
        new(-14.629215f, -44.779459f),
        new(-13.45113f, -45.200985f),
        new(-12.237402f, -45.505006f),
        new(-10.99972f, -45.688601f),
        new(0.0f, -46.349995f),
        new(10.99972f, -45.688601f),
        new(12.237402f, -45.505006f),
        new(13.45113f, -45.200985f),
        new(14.629215f, -44.779459f),
        new(15.760309f, -44.244491f),
        new(16.833522f, -43.601233f),
        new(17.838516f, -42.855879f),
        new(18.765612f, -42.015605f),
        new(19.605884f, -41.088508f),
        new(20.35124f, -40.083516f),
        new(20.994498f, -39.010304f),
        new(21.529465f, -37.87921f),
        new(21.950991f, -36.701123f),
        new(22.255013f, -35.487397f),
        new(22.438606f, -34.249713f),
        new(23.099999f, 0.0f),
        new(23.099999f, 0.000003f),
        new(22.438606f, 34.249711f),
        new(22.255013f, 35.487396f),
        new(21.950991f, 36.701122f),
        new(21.529465f, 37.879208f),
        new(20.994498f, 39.010302f),
        new(20.35124f, 40.083515f),
        new(19.605884f, 41.088506f),
        new(18.765612f, 42.015603f),
        new(17.838516f, 42.855877f),
        new(16.833522f, 43.601231f),
        new(15.760309f, 44.244489f),
        new(14.629215f, 44.779457f),
        new(13.45113f, 45.200983f),
        new(12.237402f, 45.505004f),
        new(10.99972f, 45.688599f),
        new(0.0f, 46.349993f),
        new(-10.99972f, 45.688599f),
        new(-12.237402f, 45.505004f),
        new(-13.45113f, 45.200983f),
        new(-14.629215f, 44.779457f),
        new(-15.760309f, 44.244489f),
        new(-16.833522f, 43.601231f),
        new(-17.838516f, 42.855877f),
        new(-18.765612f, 42.015603f),
        new(-19.605884f, 41.088506f),
        new(-20.35124f, 40.083515f),
        new(-20.994498f, 39.010302f),
        new(-21.529465f, 37.879208f),
        new(-21.950991f, 36.701122f),
        new(-22.255013f, 35.487396f),
        new(-22.438606f, 34.249711f),
        new(-23.099999f, 0.000003f)
    };

    public static readonly ZoneDefinition[] Zones = BuildZones();

    public sealed class ZoneDefinition
    {
        public string Id = string.Empty;
        public string Name = string.Empty;
        public Color FillColor;
        public Color OutlineColor;
        public Color HaloColor;
        public Vector2[] Polygon = Array.Empty<Vector2>();
        public int[] Triangles = Array.Empty<int>();
        public Vector3 LabelPosition;
        public PlayerTeam EndTeam = PlayerTeam.None;
    }

    private static ZoneDefinition[] BuildZones()
    {
        List<ZoneDefinition> zones = new();
        AddEnd(zones, -1f);
        AddEnd(zones, 1f);
        return zones.ToArray();
    }

    private static void AddEnd(List<ZoneDefinition> zones, float sign)
    {
        bool isRedEnd = sign < 0f;
        PlayerTeam endTeam = isRedEnd ? PlayerTeam.Red : PlayerTeam.Blue;
        AddZone(zones, "Z1", "Inner slot", Green(), endTeam, new[]
        {
            new Vector2(2.4f, sign * GoalLineZ),
            new Vector2(-2.4f, sign * GoalLineZ),
            new Vector2(-6.35f, sign * 35.15f),
            new Vector2(0.0f, sign * SlotPointZ),
            new Vector2(6.35f, sign * 35.15f)
        });

        AddZone(zones, "Z2", "Outer slot", Blue(), endTeam, new[]
        {
            new Vector2(6.35f, sign * 35.15f),
            new Vector2(10.35f, sign * 30.38f),
            new Vector2(10.35f, sign * CircleSideEdgeZ),
            new Vector2(-10.35f, sign * CircleSideEdgeZ),
            new Vector2(-10.35f, sign * 30.38f),
            new Vector2(-6.35f, sign * 35.15f),
            new Vector2(0.0f, sign * SlotPointZ)
        });

        AddZone(zones, "Z3", "Left Flank", Yellow(), endTeam, BuildFlankPolygon(sign, 1f));

        AddZone(zones, "Z4", "Right Flank", RightFlankOrange(), endTeam, BuildFlankPolygon(sign, -1f));

        AddZone(zones, "Z5", "Above Circles", Red(), endTeam, BuildAboveCirclesPolygon(sign));

        AddZone(zones, "Z6", "Below Goal Line", Purple(), endTeam, BuildBelowGoalLinePolygon(sign));
    }

    public static string GetDisplayName(ZoneDefinition zone, PlayerTeam localTeam)
    {
        if (!ShouldSwapDefendingFlank(zone, localTeam))
        {
            return zone.Name;
        }

        return zone.Id == "Z3" ? "Right Flank" : "Left Flank";
    }

    public static Color GetDisplayFillColor(ZoneDefinition zone, PlayerTeam localTeam)
    {
        Color color = ZoneOverlaySettings.GetZoneColor(GetDisplayZoneId(zone, localTeam));
        return new Color(color.r, color.g, color.b, ZoneAlpha);
    }

    public static Color GetDisplayOutlineColor(ZoneDefinition zone, PlayerTeam localTeam)
    {
        return Darken(GetDisplayFillColor(zone, localTeam));
    }

    public static Color GetDisplayTextColor(ZoneDefinition zone, PlayerTeam localTeam)
    {
        return ZoneOverlaySettings.GetZoneTextColor(GetDisplayZoneId(zone, localTeam));
    }

    public static Color GetDisplayTextOutlineColor(ZoneDefinition zone, PlayerTeam localTeam)
    {
        return ZoneOverlaySettings.GetZoneTextOutlineColor(GetDisplayZoneId(zone, localTeam));
    }

    public static Color GetDisplayHaloColor(ZoneDefinition zone, PlayerTeam localTeam)
    {
        return GetDisplayTextOutlineColor(zone, localTeam);
    }

    private static string GetDisplayZoneId(ZoneDefinition zone, PlayerTeam localTeam)
    {
        if (!ShouldSwapDefendingFlank(zone, localTeam))
        {
            return zone.Id;
        }

        return zone.Id == "Z3" ? "Z4" : "Z3";
    }

    private static bool ShouldSwapDefendingFlank(ZoneDefinition zone, PlayerTeam localTeam)
    {
        return (localTeam == PlayerTeam.Blue || localTeam == PlayerTeam.Red)
               && zone.EndTeam == localTeam
               && (zone.Id == "Z3" || zone.Id == "Z4");
    }

    private static Vector2[] BuildFlankPolygon(float endSign, float sideSign)
    {
        List<Vector2> points = new()
        {
            new Vector2(sideSign * 2.4f, endSign * GoalLineZ),
            new Vector2(sideSign * 6.35f, endSign * 35.15f),
            new Vector2(sideSign * 10.35f, endSign * 30.38f),
            new Vector2(sideSign * 10.35f, endSign * CircleSideEdgeZ)
        };
        points.AddRange(GetSideBoardPath(sideSign, endSign * CircleSideEdgeZ, endSign * GoalLineZ));
        RemoveNearDuplicates(points);
        return points.ToArray();
    }

    private static Vector2[] BuildAboveCirclesPolygon(float endSign)
    {
        float circleZ = endSign * CircleSideEdgeZ;
        float centerZ = endSign * AboveCirclesCenterEdgeZ;
        return new[]
        {
            GetSideZoneBoardPointAtZ(1f, circleZ),
            new Vector2(10.35f, circleZ),
            new Vector2(-10.35f, circleZ),
            GetSideZoneBoardPointAtZ(-1f, circleZ),
            GetSideZoneBoardPointAtZ(-1f, centerZ),
            GetSideZoneBoardPointAtZ(1f, centerZ)
        };
    }

    private static Vector2[] BuildBelowGoalLinePolygon(float endSign)
    {
        float goalZ = endSign * GoalLineZ;
        float backZ = endSign * BackBoardZ;
        List<Vector2> points = new()
        {
            GetBoardPointAtZ(-1f, goalZ),
            new Vector2(-2.4f, goalZ),
            new Vector2(2.4f, goalZ),
            GetBoardPointAtZ(1f, goalZ)
        };

        points.AddRange(GetSideBoardPath(1f, goalZ, backZ));
        points.Add(new Vector2(-BackBoardHalfWidth, backZ));
        points.AddRange(GetSideBoardPath(-1f, backZ, goalZ));
        RemoveNearDuplicates(points);
        return points.ToArray();
    }

    private static Vector2[] GetSideBoardPath(float sideSign, float startZ, float endZ)
    {
        List<Vector2> points = new()
        {
            GetSideZoneBoardPointAtZ(sideSign, startZ)
        };

        float minZ = Mathf.Min(startZ, endZ);
        float maxZ = Mathf.Max(startZ, endZ);
        List<Vector2> candidates = new();
        for (int i = 0; i < BoardHull.Length; i++)
        {
            Vector2 point = BoardHull[i];
            if (point.x * sideSign <= 0.001f || point.y <= minZ + 0.0001f || point.y >= maxZ - 0.0001f)
            {
                continue;
            }

            if (Mathf.Abs(point.y) < CornerTangentZ - 0.0001f)
            {
                continue;
            }

            candidates.Add(point);
        }

        candidates.Sort((a, b) =>
        {
            float aT = Mathf.Approximately(startZ, endZ) ? 0f : (a.y - startZ) / (endZ - startZ);
            float bT = Mathf.Approximately(startZ, endZ) ? 0f : (b.y - startZ) / (endZ - startZ);
            return aT.CompareTo(bT);
        });

        points.AddRange(candidates);
        points.Add(GetSideZoneBoardPointAtZ(sideSign, endZ));
        RemoveNearDuplicates(points);
        return points.ToArray();
    }

    private static Vector2 GetSideZoneBoardPointAtZ(float sideSign, float z)
    {
        float absZ = Mathf.Abs(z);
        if (absZ <= CornerTangentZ + 0.0001f)
        {
            float taper = Mathf.Clamp01((CornerTangentZ - absZ) / (CornerTangentZ - AboveCirclesCenterEdgeZ));
            float boardX = StraightSideBoardX + CenterSideBoardExtraX * taper;
            return new Vector2(sideSign * boardX, z);
        }

        return GetBoardPointAtZ(sideSign, z);
    }

    private static Vector2 GetBoardPointAtZ(float sideSign, float z)
    {
        bool found = false;
        float selectedX = sideSign > 0f ? float.MinValue : float.MaxValue;
        for (int i = 0; i < BoardHull.Length; i++)
        {
            Vector2 a = BoardHull[i];
            Vector2 b = BoardHull[(i + 1) % BoardHull.Length];
            float minZ = Mathf.Min(a.y, b.y);
            float maxZ = Mathf.Max(a.y, b.y);
            if (z < minZ - 0.0001f || z > maxZ + 0.0001f)
            {
                continue;
            }

            if (Mathf.Abs(a.y - b.y) < 0.0001f)
            {
                if (Mathf.Abs(z - a.y) >= 0.0001f)
                {
                    continue;
                }

                SelectBoardX(sideSign, a.x, ref selectedX, ref found);
                SelectBoardX(sideSign, b.x, ref selectedX, ref found);
                continue;
            }

            float t = Mathf.Clamp01((z - a.y) / (b.y - a.y));
            SelectBoardX(sideSign, Mathf.Lerp(a.x, b.x, t), ref selectedX, ref found);
        }

        if (!found)
        {
            selectedX = sideSign * GetBoardXExtentAtZ(z);
        }

        return new Vector2(selectedX, z);
    }

    private static void SelectBoardX(float sideSign, float x, ref float selectedX, ref bool found)
    {
        if (sideSign > 0f)
        {
            if (!found || x > selectedX)
            {
                selectedX = x;
                found = true;
            }

            return;
        }

        if (!found || x < selectedX)
        {
            selectedX = x;
            found = true;
        }
    }

    private static void AddZone(List<ZoneDefinition> zones, string id, string name, Color color, PlayerTeam endTeam, Vector2[] polygon)
    {
        if (polygon.Length < 3)
        {
            return;
        }

        int[] triangles = TriangulatePolygon(polygon);
        if (triangles.Length < 3)
        {
            return;
        }

        Vector2 centroid = GetCentroid(polygon);
        Vector2 labelPoint = id == "Z2"
            ? GetCenterlineMidpoint(polygon)
            : centroid;
        if (id == "Z6")
        {
            labelPoint = MoveTowardBackBoards(labelPoint);
        }

        zones.Add(new ZoneDefinition
        {
            Id = id,
            Name = name,
            FillColor = color,
            OutlineColor = Darken(color),
            HaloColor = new Color(color.r, color.g, color.b, 0.96f),
            Polygon = polygon,
            Triangles = triangles,
            LabelPosition = new Vector3(labelPoint.x, 1.4f, labelPoint.y),
            EndTeam = endTeam
        });
    }

    private static Color Green() => new(0.314f, 0.784f, 0.471f, ZoneAlpha);
    private static Color Blue() => new(0.388f, 0.584f, 0.933f, ZoneAlpha);
    private static Color Yellow() => new(1.0f, 0.871f, 0.129f, ZoneAlpha);
    private static Color Red() => new(0.855f, 0.173f, 0.263f, ZoneAlpha);
    private static Color Purple() => new(0.592f, 0.408f, 0.886f, ZoneAlpha);
    private static Color RightFlankOrange() => new(0.886f, 0.541f, 0.353f, ZoneAlpha);
    private static Color Darken(Color color) => new(color.r * 0.52f, color.g * 0.52f, color.b * 0.52f, color.a);

    private static Vector2 MoveTowardBackBoards(Vector2 point)
    {
        float sign = Mathf.Sign(point.y);
        if (Mathf.Approximately(sign, 0f))
        {
            return point;
        }

        float targetZ = sign * (BackBoardZ - BoardVisualInset);
        float z = Mathf.MoveTowards(point.y, targetZ, Z6LabelBackBoardShift);
        float xExtent = Mathf.Max(0f, GetBoardXExtentAtZ(z) - BoardVisualInset);
        return new Vector2(Mathf.Clamp(point.x, -xExtent, xExtent), z);
    }

    private static float GetBoardXExtentAtZ(float z)
    {
        float extent = 0f;
        for (int i = 0; i < BoardHull.Length; i++)
        {
            Vector2 a = BoardHull[i];
            Vector2 b = BoardHull[(i + 1) % BoardHull.Length];
            float minZ = Mathf.Min(a.y, b.y);
            float maxZ = Mathf.Max(a.y, b.y);
            if (z < minZ - 0.0001f || z > maxZ + 0.0001f)
            {
                continue;
            }

            if (Mathf.Abs(a.y - b.y) < 0.0001f)
            {
                if (Mathf.Abs(z - a.y) < 0.0001f)
                {
                    extent = Mathf.Max(extent, Mathf.Abs(a.x), Mathf.Abs(b.x));
                }

                continue;
            }

            float t = Mathf.Clamp01((z - a.y) / (b.y - a.y));
            float x = Mathf.Lerp(a.x, b.x, t);
            extent = Mathf.Max(extent, Mathf.Abs(x));
        }

        return extent > 0f ? extent : BoardX;
    }

    private static int[] TriangulatePolygon(IReadOnlyList<Vector2> polygon)
    {
        if (polygon.Count < 3)
        {
            return Array.Empty<int>();
        }

        List<int> remaining = new(polygon.Count);
        for (int i = 0; i < polygon.Count; i++)
        {
            remaining.Add(i);
        }

        List<int> triangles = new((polygon.Count - 2) * 3);
        bool isCounterClockwise = SignedArea(polygon) > 0f;
        int guard = polygon.Count * polygon.Count;

        while (remaining.Count > 3 && guard-- > 0)
        {
            bool clippedEar = false;
            for (int i = 0; i < remaining.Count; i++)
            {
                int previousIndex = remaining[(i + remaining.Count - 1) % remaining.Count];
                int currentIndex = remaining[i];
                int nextIndex = remaining[(i + 1) % remaining.Count];

                Vector2 previous = polygon[previousIndex];
                Vector2 current = polygon[currentIndex];
                Vector2 next = polygon[nextIndex];
                if (!IsConvex(previous, current, next, isCounterClockwise))
                {
                    continue;
                }

                if (ContainsOtherPoint(polygon, remaining, previousIndex, currentIndex, nextIndex))
                {
                    continue;
                }

                AddUpwardTriangle(triangles, polygon, previousIndex, currentIndex, nextIndex);
                remaining.RemoveAt(i);
                clippedEar = true;
                break;
            }

            if (!clippedEar)
            {
                return TriangulateFan(polygon);
            }
        }

        if (remaining.Count == 3)
        {
            AddUpwardTriangle(triangles, polygon, remaining[0], remaining[1], remaining[2]);
        }

        return triangles.ToArray();
    }

    private static int[] TriangulateFan(IReadOnlyList<Vector2> polygon)
    {
        List<int> triangles = new((polygon.Count - 2) * 3);
        for (int i = 1; i < polygon.Count - 1; i++)
        {
            AddUpwardTriangle(triangles, polygon, 0, i, i + 1);
        }

        return triangles.ToArray();
    }

    private static bool IsConvex(Vector2 previous, Vector2 current, Vector2 next, bool isCounterClockwise)
    {
        float cross = Cross(current - previous, next - current);
        return isCounterClockwise ? cross > 0.0001f : cross < -0.0001f;
    }

    private static bool ContainsOtherPoint(IReadOnlyList<Vector2> polygon, IReadOnlyList<int> remaining, int aIndex, int bIndex, int cIndex)
    {
        Vector2 a = polygon[aIndex];
        Vector2 b = polygon[bIndex];
        Vector2 c = polygon[cIndex];
        for (int i = 0; i < remaining.Count; i++)
        {
            int pointIndex = remaining[i];
            if (pointIndex == aIndex || pointIndex == bIndex || pointIndex == cIndex)
            {
                continue;
            }

            if (IsPointInsideTriangle(polygon[pointIndex], a, b, c))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsPointInsideTriangle(Vector2 point, Vector2 a, Vector2 b, Vector2 c)
    {
        float crossA = Cross(b - a, point - a);
        float crossB = Cross(c - b, point - b);
        float crossC = Cross(a - c, point - c);
        bool hasNegative = crossA < -0.0001f || crossB < -0.0001f || crossC < -0.0001f;
        bool hasPositive = crossA > 0.0001f || crossB > 0.0001f || crossC > 0.0001f;
        return !(hasNegative && hasPositive);
    }

    private static void AddUpwardTriangle(List<int> triangles, IReadOnlyList<Vector2> polygon, int aIndex, int bIndex, int cIndex)
    {
        float cross = Cross(polygon[bIndex] - polygon[aIndex], polygon[cIndex] - polygon[aIndex]);
        if (Mathf.Abs(cross) < 0.0001f)
        {
            return;
        }

        if (cross > 0f)
        {
            triangles.Add(aIndex);
            triangles.Add(cIndex);
            triangles.Add(bIndex);
            return;
        }

        triangles.Add(aIndex);
        triangles.Add(bIndex);
        triangles.Add(cIndex);
    }

    private static Vector2 GetCentroid(IReadOnlyList<Vector2> polygon)
    {
        float twiceArea = 0f;
        float x = 0f;
        float z = 0f;

        for (int i = 0; i < polygon.Count; i++)
        {
            Vector2 a = polygon[i];
            Vector2 b = polygon[(i + 1) % polygon.Count];
            float cross = Cross(a, b);
            twiceArea += cross;
            x += (a.x + b.x) * cross;
            z += (a.y + b.y) * cross;
        }

        if (Mathf.Abs(twiceArea) < 0.000001f)
        {
            return polygon[0];
        }

        float factor = 1f / (3f * twiceArea);
        return new Vector2(x * factor, z * factor);
    }

    private static Vector2 GetCenterlineMidpoint(IReadOnlyList<Vector2> polygon)
    {
        List<float> intersections = new();
        for (int i = 0; i < polygon.Count; i++)
        {
            Vector2 a = polygon[i];
            Vector2 b = polygon[(i + 1) % polygon.Count];
            if (Mathf.Abs(a.x) < 0.0001f)
            {
                intersections.Add(a.y);
            }

            if ((a.x < 0f && b.x > 0f) || (a.x > 0f && b.x < 0f))
            {
                float t = -a.x / (b.x - a.x);
                intersections.Add(Mathf.Lerp(a.y, b.y, t));
            }
        }

        if (intersections.Count < 2)
        {
            return GetCentroid(polygon);
        }

        intersections.Sort();
        return new Vector2(0f, (intersections[0] + intersections[intersections.Count - 1]) * 0.5f);
    }

    private static void RemoveNearDuplicates(List<Vector2> points)
    {
        for (int i = points.Count - 1; i > 0; i--)
        {
            if ((points[i] - points[i - 1]).sqrMagnitude < 0.000001f)
            {
                points.RemoveAt(i);
            }
        }

        if (points.Count > 1 && (points[0] - points[points.Count - 1]).sqrMagnitude < 0.000001f)
        {
            points.RemoveAt(points.Count - 1);
        }
    }

    private static float SignedArea(IReadOnlyList<Vector2> polygon)
    {
        float area = 0f;
        for (int i = 0; i < polygon.Count; i++)
        {
            area += Cross(polygon[i], polygon[(i + 1) % polygon.Count]);
        }

        return area * 0.5f;
    }

    private static float Cross(Vector2 a, Vector2 b)
    {
        return a.x * b.y - a.y * b.x;
    }
}
