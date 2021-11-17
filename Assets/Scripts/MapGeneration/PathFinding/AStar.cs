#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;
using UnityEngine;

namespace Dora.MapGeneration {
    public class AStar : IPathFinder {
        
        private class AStarTile {
            public readonly int X, Y;
            public AStarTile? Parent;
            public readonly float Heuristic;
            public readonly float Cost;
            public readonly float TotalCost;

            public AStarTile(int x, int y, AStarTile? parent, float heuristic, float cost) {
                X = x;
                Y = y;
                this.Parent = parent;
                Heuristic = heuristic;
                this.Cost = cost;
                this.TotalCost = cost + heuristic;
            }
            
            public List<Vector2Int> Path() {
                var path = new List<Vector2Int>();

                AStarTile? current = this;
                while (current != null) {
                    path.Add(new Vector2Int(current.X, current.Y));
                    current = current.Parent;
                }

                path.Reverse();
                return path;
            }
        }

        public List<Vector2Int>? GetOptimisticPath(Vector2Int startCoordinate, Vector2Int targetCoordinate,
            IPathFindingMap pathFindingMap, bool acceptPartialPaths = false) {
            return GetPath(startCoordinate, targetCoordinate, pathFindingMap, true, acceptPartialPaths);
        }

        public List<Vector2Int>? GetPath(Vector2Int startCoordinate, Vector2Int targetCoordinate, IPathFindingMap pathFindingMap, bool beOptimistic = false, bool acceptPartialPaths = false) {
            var candidates = new List<AStarTile>();
            var bestCandidateOnTile = new Dictionary<Vector2Int, AStarTile>();
            var startTileHeuristic = OctileHeuristic(startCoordinate, targetCoordinate);
            var startingTile = new AStarTile(startCoordinate.x, startCoordinate.y, null, startTileHeuristic, 0);
            candidates.Add(startingTile);
            bestCandidateOnTile[startCoordinate] = startingTile;

            int loopCount = 0; 
            while (candidates.Count > 0) {
                var currentTile = DequeueBestCandidate(candidates);
                
                var currentCoordinate = new Vector2Int(currentTile.X, currentTile.Y);
                if (currentCoordinate == targetCoordinate)
                    return currentTile.Path();

                foreach (var dir in CardinalDirection.AllDirections()) {
                    Vector2Int candidateCoord = currentCoordinate + dir.DirectionVector;
                    // Only consider non-solid tiles
                    if (IsSolid(candidateCoord, pathFindingMap, beOptimistic) && candidateCoord != targetCoordinate) continue;

                    if (dir.IsDiagonal()) {
                        // To travel diagonally, the two neighbouring tiles must also be free
                        if (IsSolid(currentCoordinate + dir.Previous().DirectionVector, pathFindingMap, beOptimistic)
                        || IsSolid(currentCoordinate + dir.Next().DirectionVector, pathFindingMap, beOptimistic))
                            continue;
                    }
                    
                    var cost = currentTile.Cost + Vector2Int.Distance(currentCoordinate, candidateCoord);
                    var heuristic = OctileHeuristic(candidateCoord, targetCoordinate);
                    var candidateCost = cost + heuristic;
                    // Check if this path is 'cheaper' than any previous path to this candidate tile 
                    if (!bestCandidateOnTile.ContainsKey(candidateCoord) || bestCandidateOnTile[candidateCoord].TotalCost > candidateCost) {
                        var newTile = new AStarTile(candidateCoord.x, candidateCoord.y, currentTile, heuristic, cost);
                        // Remove previous best entry if present
                        if(bestCandidateOnTile.ContainsKey(candidateCoord))
                            candidates.Remove(bestCandidateOnTile[candidateCoord]);
                        // Save this as the new best candidate for this tile
                        bestCandidateOnTile[candidateCoord] = newTile;
                        candidates.Add(newTile);
                    }
                }

                if (loopCount > 10000) {
                    throw new Exception("A* could not find path within 10000 loop runs");
                }

                loopCount++;
            }
            
            if (acceptPartialPaths) {
                // Find lowest heuristic tile, as it is closest to the target
                Vector2Int? lowestHeuristicKey = null;
                float lowestHeuristic = float.MaxValue;
                foreach (var kv in bestCandidateOnTile) {
                    if (kv.Value.Heuristic < lowestHeuristic) {
                        lowestHeuristic = kv.Value.Heuristic;
                        lowestHeuristicKey = kv.Key;
                    }
                }

                var closestTile = bestCandidateOnTile[lowestHeuristicKey.Value];
                return GetPath(startCoordinate, new Vector2Int(closestTile.X, closestTile.Y),
                    pathFindingMap, beOptimistic, false);
            }

            return null;
        }

        private AStarTile DequeueBestCandidate(List<AStarTile> candidates) {
            AStarTile bestCandidate = candidates.First();
            foreach (var current in candidates.Skip(1)) {
                if (Mathf.Abs(current.TotalCost - bestCandidate.TotalCost) < 0.01f) {
                    // Total cost is the same, compare by heuristic instead
                    if (current.Heuristic < bestCandidate.Heuristic)
                        bestCandidate = current;
                } else if (current.TotalCost < bestCandidate.TotalCost) {
                    bestCandidate = current;
                }
            }

            candidates.Remove(bestCandidate);
            return bestCandidate;
        }

        private bool IsSolid(Vector2Int coord, IPathFindingMap map, bool optimistic) {
            return optimistic
                ? map.IsOptimisticSolid(coord) 
                : map.IsSolid(coord); 
        }

        private float OctileHeuristic(Vector2Int from, Vector2Int to) {
            var xDif = Math.Abs(from.x - to.x);
            var yDif = Math.Abs(from.y - to.y);
            
            var minDif = Math.Min(xDif, yDif);
            var maxDif = Math.Max(xDif, yDif);
            
            float heuristic = maxDif - minDif + minDif * Mathf.Sqrt(2f);;
            return heuristic;
        }
        
        
        public List<Vector2Int> GetIntersectingTiles(List<Vector2Int> path, float robotRadius) {
            throw new System.NotImplementedException();
        }

        
    }
}