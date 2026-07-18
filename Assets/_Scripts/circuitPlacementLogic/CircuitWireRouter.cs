using System;
using System.Collections.Generic;
using UnityEngine;

public static class CircuitWireRouter
{
    private const int StraightCost = 10;
    private const int DiagonalCost = 14;
    private const int TurnCost = 2;

    private static readonly Vector2Int[] Directions =
    {
        new(1, 0), new(0, 1), new(-1, 0), new(0, -1),
        new(1, 1), new(-1, 1), new(-1, -1), new(1, -1)
    };

    public static bool TryFindPath(
        int width,
        int height,
        Vector2Int start,
        Vector2Int goal,
        Func<Vector2Int, bool> isBlocked,
        Func<Vector2Int, Vector2Int, bool> isMoveBlocked,
        out List<Vector2Int> path)
    {
        path = null;
        int count = width * height;
        int[] costs = new int[count];
        int[] parents = new int[count];
        sbyte[] arrivalDirections = new sbyte[count];
        bool[] closed = new bool[count];
        List<int> open = new(count);
        Array.Fill(costs, int.MaxValue);
        Array.Fill(parents, -1);
        Array.Fill(arrivalDirections, (sbyte)-1);

        int startIndex = ToIndex(start, width);
        int goalIndex = ToIndex(goal, width);
        costs[startIndex] = 0;
        open.Add(startIndex);

        while (open.Count > 0)
        {
            int bestOpenIndex = 0;
            int currentIndex = open[0];
            int bestScore = costs[currentIndex] + Heuristic(ToCell(currentIndex, width), goal);

            for (int i = 1; i < open.Count; i++)
            {
                int candidate = open[i];
                int score = costs[candidate] + Heuristic(ToCell(candidate, width), goal);
                if (score < bestScore || score == bestScore && candidate < currentIndex)
                {
                    bestOpenIndex = i;
                    currentIndex = candidate;
                    bestScore = score;
                }
            }

            open.RemoveAt(bestOpenIndex);
            if (closed[currentIndex])
                continue;
            if (currentIndex == goalIndex)
            {
                path = Reconstruct(goalIndex, parents, width);
                return true;
            }

            closed[currentIndex] = true;
            Vector2Int current = ToCell(currentIndex, width);
            for (sbyte directionIndex = 0; directionIndex < Directions.Length; directionIndex++)
            {
                Vector2Int direction = Directions[directionIndex];
                Vector2Int next = current + direction;
                if (!IsInside(next, width, height))
                    continue;

                if (isBlocked(next))
                    continue;
                if (isMoveBlocked != null && isMoveBlocked(current, next))
                    continue;

                if (direction.x != 0 && direction.y != 0 &&
                    (isBlocked(current + new Vector2Int(direction.x, 0)) ||
                     isBlocked(current + new Vector2Int(0, direction.y))))
                    continue;

                int nextIndex = ToIndex(next, width);
                int moveCost = direction.x != 0 && direction.y != 0 ? DiagonalCost : StraightCost;
                if (arrivalDirections[currentIndex] >= 0 && arrivalDirections[currentIndex] != directionIndex)
                    moveCost += TurnCost;

                int newCost = costs[currentIndex] + moveCost;
                if (newCost >= costs[nextIndex])
                    continue;

                costs[nextIndex] = newCost;
                parents[nextIndex] = currentIndex;
                arrivalDirections[nextIndex] = directionIndex;
                open.Add(nextIndex);
            }
        }

        return false;
    }

    public static List<Vector2Int> BuildDirectPath(Vector2Int start, Vector2Int goal)
    {
        List<Vector2Int> path = new() { start };
        Vector2Int current = start;
        while (current != goal)
        {
            current += new Vector2Int(
                Math.Sign(goal.x - current.x),
                Math.Sign(goal.y - current.y));
            path.Add(current);
        }

        return path;
    }

    public static List<Vector2Int> Compress(IReadOnlyList<Vector2Int> path)
    {
        List<Vector2Int> result = new();
        if (path == null || path.Count == 0)
            return result;

        result.Add(path[0]);
        if (path.Count == 1)
            return result;

        Vector2Int previousDirection = path[1] - path[0];
        for (int i = 1; i < path.Count - 1; i++)
        {
            Vector2Int nextDirection = path[i + 1] - path[i];
            if (nextDirection != previousDirection)
                result.Add(path[i]);
            previousDirection = nextDirection;
        }

        result.Add(path[^1]);
        return result;
    }

    private static int Heuristic(Vector2Int from, Vector2Int to)
    {
        int dx = Mathf.Abs(to.x - from.x);
        int dy = Mathf.Abs(to.y - from.y);
        return DiagonalCost * Mathf.Min(dx, dy) + StraightCost * Mathf.Abs(dx - dy);
    }

    private static List<Vector2Int> Reconstruct(int index, int[] parents, int width)
    {
        List<Vector2Int> result = new();
        while (index >= 0)
        {
            result.Add(ToCell(index, width));
            index = parents[index];
        }
        result.Reverse();
        return result;
    }

    private static bool IsInside(Vector2Int cell, int width, int height)
        => cell.x >= 0 && cell.y >= 0 && cell.x < width && cell.y < height;

    private static int ToIndex(Vector2Int cell, int width)
        => cell.y * width + cell.x;

    private static Vector2Int ToCell(int index, int width)
        => new(index % width, index / width);
}
