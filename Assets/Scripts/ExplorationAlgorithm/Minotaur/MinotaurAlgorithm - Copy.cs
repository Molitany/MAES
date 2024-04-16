using Maes.Map;
using Maes.Robot;
using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using Maes.Utilities;
using static Maes.Map.SlamMap;
using System.Reflection;

namespace Maes.ExplorationAlgorithm.Minotaur
{
    public partial class MinotaurAlgorithmCopy : IExplorationAlgorithm
    {
        public int VisionRadius => (int)_robotConstraints.SlamRayTraceRange;
        private int _doorWidth;

        private IRobotController _controller;
        private RobotConstraints _robotConstraints;
        private CoarseGrainedMap _map;
        private EdgeDetector _edgeDetector;
        private Dictionary<Vector2Int, SlamTileStatus> _visibleTiles => _controller.GetSlamMap().GetCurrentlyVisibleTiles();
        private int _seed;
        private Vector2Int _position => _map.GetCurrentPosition();
        private List<Doorway> _doorways = new();
        private List<MinotaurAlgorithm> _minotaurs;
        private bool _clockwise = false;
        private AlgorithmState _currentState = AlgorithmState.Idle;
        private DoorState _doorState = DoorState.None;
        private bool _taskBegun;
        private Doorway _closestDoorway = null;
        private Waypoint? _waypoint;
        private Vector2Int? _lastWallTile;
        private List<Line2D> _lastWalls = new();
        private int _logicTicks = 0;

        private enum AlgorithmState
        {
            Idle,
            FirstWall,
            ExploreRoom,
            Auctioning,
            MovingToDoorway,
            MovingToNearestUnexplored,
            Done
        }
        private enum DoorState
        {
            None,
            Single,
            Intersection
        }

        private struct Waypoint
        {
            public Vector2Int Destination;
            public bool UsePathing;
            public WaypointType Type;

            public enum WaypointType
            {
                Wall,
                Corner,
                Edge,
                Greed,
                Door
            }

            public Waypoint(Vector2Int destination, WaypointType type, bool pathing = false)
            {
                Destination = destination;
                Type = type;
                UsePathing = pathing;
            }
        }

        private struct RelativeWall
        {
            public Vector2Int Position;
            public float Distance;
            public float Angle;
        }

        public MinotaurAlgorithmCopy(RobotConstraints robotConstraints, int seed, int doorWidth)
        {
            _robotConstraints = robotConstraints;
            _seed = seed;
            _doorWidth = doorWidth;
            Doorway.DoorWidth = _doorWidth;
        }

        public string GetDebugInfo()
        {
            return $"State: {Enum.GetName(typeof(AlgorithmState), _currentState)}" +
                   $"\nCoarse Map Position: {_map.GetApproximatePosition()}";
        }

        public void SetController(Robot2DController controller)
        {
            _controller = controller;
            _map = _controller.GetSlamMap().GetCoarseMap();
            Doorway._map = _map;
            _edgeDetector = new EdgeDetector(_controller.GetSlamMap(), VisionRadius);
        }

        public void UpdateLogic()
        {
            _logicTicks++;
            if (_controller.IsCurrentlyColliding())
            {
                if (_controller.GetStatus() != Robot.Task.RobotStatus.Idle)
                    _controller.StopCurrentTask();
                else
                    _controller.Move(1, true);
                _waypoint = null;
                return;
                //TODO: full resets
            }

            var wallPoints = GetWallsNearRobot();

            switch (_doorState)
            {
                case DoorState.None:
                    if (_waypoint.HasValue && _waypoint.Value.Type == Waypoint.WaypointType.Wall)// || !_waypoint.HasValue)
                        _doorState = DoorwayDetection(wallPoints);
                    if (_doorState != DoorState.None)
                        _lastWalls = GetWalls(wallPoints.Select(tile => tile.Position));
                    break;
                case DoorState.Single:
                    if (IsDestinationReached())
                    {
                        _doorState = DoorState.None;
                        if (!wallPoints.Any()) break;
                        var walls = GetWalls(wallPoints.Select(tile => tile.Position));
                        walls.ToList().ForEach(wall => Debug.DrawLine(_map.TileToWorld(wall.Start), _map.TileToWorld(wall.End)));
                        if (walls.Any(wall => _lastWalls.Contains(wall)))
                        {
                            var (start, end) = SortSingleWall(walls);
                            var wallDirectionVector = CardinalDirection.DirectionFromDegrees((end - start).GetAngleRelativeToX()).Vector;
                            var closestWall = wallPoints.First();
                            var approachDirection = _clockwise ? CardinalDirection.DirectionFromDegrees((_controller.GetGlobalAngle() + 90) % 360) : CardinalDirection.DirectionFromDegrees((_controller.GetGlobalAngle() + 270) % 360);
                            var newDoor = new Doorway(_lastWallTile.Value, closestWall.Position, approachDirection);
                            if (!_doorways.Any(doorway => newDoor.Equals(doorway)))
                            {
                                _closestDoorway = newDoor;
                                _doorways.Add(_closestDoorway);
                            }
                        }
                    }
                    break;
                case DoorState.Intersection:
                    break;
                default:
                    break;
            }

            if (_waypoint.HasValue)
            {
                var waypoint = _waypoint.Value;

                if (waypoint.UsePathing)
                {
                    _controller.PathAndMoveTo(waypoint.Destination);
                }
                else
                {
                    var solidTile = _edgeDetector.GetFurthestTileAroundRobot((waypoint.Destination - _position).GetAngleRelativeToX(), VisionRadius - 2, new List<SlamTileStatus> { SlamTileStatus.Solid }, true);
                    if (_map.GetTileStatus(solidTile) == SlamTileStatus.Solid)
                    {
                        _waypoint = null;
                        _controller.StopCurrentTask();
                        return;
                    }
                    _controller.MoveTo(waypoint.Destination);
                }
                if (IsDestinationReached())
                {
                    _waypoint = null;
                }
                return;
            }

            switch (_currentState)
            {
                case AlgorithmState.Idle:
                    _controller.StartMoving();
                    _currentState = AlgorithmState.FirstWall;
                    break;
                case AlgorithmState.FirstWall:
                    if (wallPoints.Any() && wallPoints.First().Distance/2 < VisionRadius - 1)
                    {
                        _controller.StopCurrentTask();
                        _currentState = AlgorithmState.ExploreRoom;
                    }
                    break;
                case AlgorithmState.ExploreRoom:
                    if (_controller.GetStatus() == Robot.Task.RobotStatus.Idle)
                    {
                        if (!IsAroundExplorable(2))
                        {
                            MoveToNearestUnseenWithinRoom();
                            break;
                        }
                        else if (MoveAlongWall()) break;
                        else if (MoveToCornerCoverage()) break;
                        else if (MoveToNearestEdge()) break;
                        else MoveToNearestUnseenWithinRoom();
                    }
                    break;
                case AlgorithmState.Auctioning:
                    break;
                case AlgorithmState.MovingToDoorway:
                    var nearestDoorway = GetNearestUnexploredDoorway();
                    if (nearestDoorway != null)
                    {
                        _controller.PathAndMoveTo(nearestDoorway.Center);
                        _waypoint = new Waypoint(nearestDoorway.Center, Waypoint.WaypointType.Door, true);
                        _currentState = AlgorithmState.ExploreRoom;
                        nearestDoorway.Explored = true;
                    }
                    else
                    {
                        _currentState = AlgorithmState.Done;
                    }
                    break;
                case AlgorithmState.Done:
                    break;
                default:
                    break;
            }
        }
        private bool MoveAlongWall()
        {
            // Wait until SlamMap has updated otherwise we see old data
            if (_logicTicks % _robotConstraints.SlamUpdateIntervalInTicks != 0)
            {
                return true;
            }
            var slamMap = _controller.GetSlamMap();
            var tiles = _edgeDetector.GetTilesAroundRobot(VisionRadius + 2, new List<SlamTileStatus> { SlamTileStatus.Solid }, slamPrecision: true)
                                     .Where(tile => slamMap.GetTileStatus(tile) == SlamTileStatus.Solid)
                                     .ToList();
            if (tiles.Count() < 2) return false;
            var tileAhead = _edgeDetector.GetFurthestTileAroundRobot(_controller.GetGlobalAngle(), VisionRadius, new List<SlamTileStatus> { SlamTileStatus.Solid }, snapToGrid: true, slamPrecision: true);
            var localLeft = (_controller.GetGlobalAngle() + (slamMap.GetTileStatus(tileAhead) == SlamTileStatus.Solid ? 90 : 0)) % 360;
            (CardinalDirection.AngleToDirection(localLeft).Vector + _position).DrawDebugLineFromRobot(_map, Color.blue);
            var walls = GetWalls(tiles);
            var wallPoints = walls.Select(wall => Vector2Int.FloorToInt(wall.Start))
                              .Union(walls.Select(wall => Vector2Int.FloorToInt(wall.End)))
                              .OrderByDescending(point => ((point - _position).GetAngleRelativeToX() - localLeft + 360) % 360).ToList();
            var wallCoarsePoints = wallPoints.Select(point => _map.FromSlamMapCoordinate(point));

            walls.ToList().ForEach(wall => Debug.DrawLine(slamMap.TileToWorld(wall.Start), slamMap.TileToWorld(wall.End), Color.white, 2));
            wallCoarsePoints.ToList().ForEach(point => point.DrawDebugLineFromRobot(_map, Color.yellow));

            var points = wallCoarsePoints.Select(point => (perp: point + CardinalDirection.PerpendicularDirection(point - _position).Vector * (VisionRadius - 2), point))
                           .Where(tuple => slamMap.IsWithinBounds(tuple.perp)
                                           && slamMap.GetTileStatus(tuple.perp) != SlamTileStatus.Solid)
                           .ToList();

            if (!points.Any())
            {
                return false;
            }


            var perpendicularTile = points.First();
            //var thirdPoint = new Vector2Int((int)(perpendicularTile.perp.x + (Math.Abs(perpendicularTile.perp.x - perpendicularTile.point.x))*(Vector2.Angle(_position,perpendicularTile.perp)/90)), (int)(perpendicularTile.perp.y + (Math.Abs(perpendicularTile.perp.y - perpendicularTile.point.y))*(1-Vector2.Angle(_position,perpendicularTile.perp)/90)));

            var perpDirection = (perpendicularTile.perp - _position).GetAngleRelativeToX();
            var thirdPointDirection = (perpDirection + 270) % 360;
            var thirdPointDirectionVector = CardinalDirection.AngleToDirection(thirdPointDirection).Vector;
            var thirdPoint = perpendicularTile.perp + thirdPointDirectionVector * (VisionRadius - 2);
            thirdPoint.DrawDebugLineFromRobot(_map, Color.blue);

            thirdPointDirectionVector = CardinalDirection.AngleToDirection((thirdPoint - perpendicularTile.point).GetAngleRelativeToX()).Vector;
            var thirdPointDirectionVectorPerpendicular = CardinalDirection.PerpendicularDirection(thirdPointDirectionVector).Vector;
            for (int i = 1; i < 4; i++)
            {
                var possibleUnseen = thirdPoint + (thirdPointDirectionVector * i) + thirdPointDirectionVectorPerpendicular;
                if (slamMap.IsWithinBounds(possibleUnseen) && slamMap.GetTileStatus(possibleUnseen) == SlamTileStatus.Unseen)
                {
                    perpendicularTile.perp.DrawDebugLineFromRobot(_map, Color.red);
                    _waypoint = new Waypoint(perpendicularTile.perp, Waypoint.WaypointType.Wall);
                    _controller.MoveTo(perpendicularTile.perp); //THIS FUCKED EVERYTHING
                    (CardinalDirection.AngleToDirection(0).Vector + _position).DrawDebugLineFromRobot(_map, Color.magenta);
                    return true;
                };
            }
            return false;
        }

        private List<Line2D> GetWalls(IEnumerable<Vector2Int> tiles)
        {
            var correctedTiles = new List<(Vector2Int corrected, Vector2Int original)>();
            var slamMap = _controller.GetSlamMap();
            var slamPosition = slamMap.GetCurrentPosition();
            foreach (var tile in tiles)
            {
                var correctedTile = tile;
                if (slamMap.GetTileStatus(tile + CardinalDirection.East.Vector) == SlamTileStatus.Open)
                    correctedTile = tile + CardinalDirection.East.Vector;
                else if (slamMap.GetTileStatus(tile + CardinalDirection.North.Vector) == SlamTileStatus.Open)
                    correctedTile = tile + CardinalDirection.North.Vector;
                else if (slamMap.GetTileStatus(tile + CardinalDirection.NorthEast.Vector) == SlamTileStatus.Open)
                    correctedTile = tile + CardinalDirection.NorthEast.Vector;

                correctedTiles.Add((correctedTile, tile));
            }

            // Get every possible line from the points we have gathered
            List<(Line2D corrected, Line2D original)> lines = new();
            foreach (var tile in correctedTiles)
            {
                foreach (var otherTile in correctedTiles)
                {
                    if (tile != otherTile)
                    {
                        var otherLine = new Line2D(tile.corrected, otherTile.corrected);
                        if (!lines.Any(line => line.corrected.EqualLineSegment(otherLine)))
                        {
                            lines.Add((otherLine, new Line2D(tile.original, otherTile.original)));
                        }
                    }
                }
            }

            // Remove the lines that have gaps of unsolid tiles
            List<Line2D> unbrokenWalls = new();
            foreach (var line in lines)
            {
                var points = line.original.Rasterize();
                //if (Mathf.Approximately(line.Start.x, 2) && Mathf.Approximately(line.End.x, 3))
                //{
                //    var a = 1;
                //}
                if (points.All(tile => _controller.GetSlamMap().GetTileStatus(Vector2Int.FloorToInt(tile)) == SlamTileStatus.Solid))
                {
                    unbrokenWalls.Add(line.corrected);
                }
            }

            // Take only the lines that are not subsets of other lines
            var superLines = new List<Line2D>();
            foreach (var line in unbrokenWalls)
            {
                var otherLines = unbrokenWalls.Where(otherLine => line != otherLine);
                if (!otherLines.Any(otherline => otherline.Contains(line)))
                {
                    superLines.Add(line);
                }
            }

            return superLines.ToList();
        }


        private bool MoveToCornerCoverage()
        {
            if (IsAheadExploreable(1) && IsAheadExploreable(2)) return false;
            var tiles = _edgeDetector.GetTilesAroundRobot(VisionRadius + 2, new List<SlamTileStatus> { SlamTileStatus.Unseen, SlamTileStatus.Solid }, startAngle: 360 - 30);
            foreach (var tile in tiles)
            {
                tile.DrawDebugLineFromRobot(_map, Color.green);
            }
            // Take 30% of tiles to the right of the robots forwarding direction and filter them for unseen only
            var cornerCoverage = tiles.Where(tile => _map.GetTileStatus(tile) == SlamTileStatus.Unseen)
                                      .ToList();
            if (cornerCoverage.Any())
            {
                var location = cornerCoverage.First();
                foreach (var tile in cornerCoverage)
                {
                    tile.DrawDebugLineFromRobot(_map, Color.magenta);
                }
                _controller.MoveTo(location);
                _waypoint = new Waypoint(location, Waypoint.WaypointType.Corner);
                return true;
            }
            return false;
        }

        private bool MoveToNearestEdge()
        {
            var tiles = _edgeDetector.GetBoxAroundRobot()
                              .Where(tile => (_map.GetTileStatus(tile) != SlamTileStatus.Unseen)
                                             && IsPerpendicularUnseen(tile))
                              .Select(tile => (status: _map.GetTileStatus(tile), perpendicularTile: tile + CardinalDirection.PerpendicularDirection(tile - _position).Vector * (VisionRadius - 1)))
                              .Where(tile => _map.IsWithinBounds(tile.perpendicularTile) && IfSolidIsPathable(tile))
                              .Select(tile => tile.perpendicularTile);
            if (tiles.Any())
            {
                // Take candidateTile from local right sweeping counter clockwise
                var localRight = _controller.GetGlobalAngle() + 180 % 360;
                var closestTile = tiles.OrderBy(tile => ((tile - _position).GetAngleRelativeToX() - localRight + 360) % 360).First();
                var angle = Vector2.SignedAngle(Vector2.right, closestTile - _position);
                var vector = Geometry.VectorFromDegreesAndMagnitude(angle, VisionRadius + 1);
                closestTile = Vector2Int.FloorToInt(vector + _position);
                foreach (var tile in tiles)
                {
                    tile.DrawDebugLineFromRobot(_map, Color.yellow);
                }
                closestTile.DrawDebugLineFromRobot(_map, Color.white);
                _controller.MoveTo(closestTile);
                _waypoint = new Waypoint(closestTile, Waypoint.WaypointType.Edge);
                return true;
            }
            return false;
        }

        private bool IsPerpendicularUnseen(Vector2Int tile)
        {
            var direction = CardinalDirection.PerpendicularDirection(tile - _position).Vector;
            var perp = direction + tile;
            if (!_map.IsWithinBounds(perp)) return false;
            return _map.GetTileStatus(perp) == SlamTileStatus.Unseen;
        }
        private bool IfSolidIsPathable((SlamTileStatus status, Vector2Int perpendicularTile) tile)
        {
            if (tile.status != SlamTileStatus.Solid) return true;
            var path = _map.GetPath(tile.perpendicularTile, false, false);
            if (path == null)
            {
                return false;
            }
            return true;
        }

        private void MoveToNearestUnseenWithinRoom()
        {
            var doorTiles = _doorways.SelectMany(doorway => doorway.Tiles);
            var tile = _map.GetNearestTileFloodFill(_position, SlamTileStatus.Unseen, doorTiles.ToHashSet());
            if (tile.HasValue)
            {
                tile = _map.GetNearestTileFloodFill(tile.Value, SlamTileStatus.Open);
                if (tile.HasValue)
                {
                    _controller.PathAndMoveTo(tile.Value);
                    _waypoint = new Waypoint(tile.Value, Waypoint.WaypointType.Greed, true);
                }
            }
            else
            {
                _currentState = AlgorithmState.MovingToDoorway;
            }
        }

        private bool IsDestinationReached()
        {
            return _waypoint.HasValue && _map.GetTileCenterRelativePosition(_waypoint.Value.Destination).Distance < 0.5f;
        }

        private bool IsAheadExploreable(int range)
        {
            var direction = CardinalDirection.AngleToDirection(_controller.GetGlobalAngle());
            var target = direction.Vector * (VisionRadius + range) + _position;
            if (_map.IsWithinBounds(target))
            {
                return _map.GetTileStatus(target) == SlamTileStatus.Unseen;
            }
            return false;
        }

        private bool IsAroundExplorable(int range)
        {
            return _edgeDetector.GetTilesAroundRobot(VisionRadius + range, new List<SlamTileStatus> { SlamTileStatus.Solid })
                                .Count(tile => _map.GetTileStatus(tile) == SlamTileStatus.Unseen) > 0;
        }

        private List<RelativeWall> GetWallsNearRobot()
        {
            return _visibleTiles.Where(kv => kv.Value == SlamTileStatus.Solid)
                                     .Distinct()
                                     .Select(kv =>
                                         new RelativeWall
                                         {
                                             Position = kv.Key,
                                             Distance = Vector2.Distance(kv.Key, _controller.GetSlamMap().GetCurrentPosition()),
                                             Angle = ((kv.Key - _position).GetAngleRelativeToX() - _controller.GetGlobalAngle() + 360) % 360
                                         }
                                     )
                                     .OrderBy(dist => dist.Distance)
                                     .ToList();
        }

        private DoorState DoorwayDetection(List<RelativeWall> visibleWallTiles)
        {
            if (!visibleWallTiles.Any()) return DoorState.None;
            var visibleTiles = _visibleTiles.Select(kv => _map.FromSlamMapCoordinate(kv.Key)).Distinct();
            var wallTilePositions = visibleWallTiles.Select(wall => wall.Position);
            var walls = GetWalls(wallTilePositions);

            if (walls.Count == 1)
            {
                var (start, end) = SortSingleWall(walls);
                var wallDirectionVector = CardinalDirection.DirectionFromDegrees((end - start).GetAngleRelativeToX()).Vector;
                var hasOpening = Enumerable.Range(0, VisionRadius * 2)
                          .Select(r => Vector2Int.FloorToInt(start + wallDirectionVector * r))
                          .Where(tile => visibleTiles.Contains(tile))
                          .Any(tile => _map.GetTileStatus(tile) == SlamTileStatus.Open);

                if (hasOpening)
                {
                    end.DrawDebugLineFromRobot(_map, Color.magenta);
                    var distanceToEnd = (end - _position) * new Vector2Int(Mathf.Abs(wallDirectionVector.x), Mathf.Abs(wallDirectionVector.y));
                    _waypoint = new Waypoint(_position + distanceToEnd + wallDirectionVector * _doorWidth, Waypoint.WaypointType.Door);
                    _controller.MoveTo(_waypoint.Value.Destination);
                    _waypoint.Value.Destination.DrawDebugLineFromRobot(_map, Color.green);
                    _lastWallTile = end;
                    return DoorState.Single;
                }
            }

            return DoorState.None;
        }

        private (Vector2Int start, Vector2Int end) SortSingleWall(List<Line2D> walls)
        {
            var wall = walls.First();
            var orderedPoints = new List<Vector2Int> { Vector2Int.FloorToInt(wall.Start), Vector2Int.FloorToInt(wall.End) }
                            .OrderBy(tile => ((tile - _position).GetAngleRelativeToX() - (_controller.GetGlobalAngle() + 180) % 360 + 360) % 360);
            return (orderedPoints.First(), orderedPoints.Last());

        }

        //private void Communication()
        //{
        //    var messages = _controller.ReceiveBroadcast().OfType<IMinotaurMessage>();
        //    var minotaurMessages = new List<IMinotaurMessage>();
        //    foreach (var message in messages)
        //    {
        //        var combined = false;
        //        foreach (var minotaurMessage in minotaurMessages)
        //        {
        //            var combinedMessage = minotaurMessage.Combine(message, this);
        //            if (combinedMessage != null)
        //            {
        //                minotaurMessages[minotaurMessages.IndexOf(message)] = combinedMessage;
        //                combined = true;
        //                break;
        //            }
        //        }
        //        if (!combined)
        //        {
        //            minotaurMessages.Add(message);
        //        }
        //    }
        //}


        private Doorway? GetNearestUnexploredDoorway()
        {
            float doorwayDistance = float.MaxValue;
            Doorway nearestDoorway = null;
            var unexploredDoorways = _doorways.Where(doorway => !doorway.Explored);
            foreach (Doorway doorway in unexploredDoorways)
            {
                var distance = PathDistanceToPoint(doorway.Center);
                if (distance < doorwayDistance)
                {
                    doorwayDistance = distance;
                    nearestDoorway = doorway;
                };
            }
            return nearestDoorway;
        }

        /// <summary>
        /// Gets the real distance that the robot has to go to on the path to the point
        /// </summary>
        /// <param name="point">The point that gets pathed to</param>
        /// <returns>The distance of the path</returns>
        private float PathDistanceToPoint(Vector2Int point)
        {
            List<Vector2Int> path = _controller.GetSlamMap().GetPath(_position, point);
            List<float> distances = new();
            for (var i = 0; i < path.Count - 2; i++)
            {
                var pathStep = path[i];
                if (pathStep == path.First())
                {
                    distances.Add(Vector2.Distance(path.First(), _position));
                }
                distances.Add(Vector2.Distance(pathStep, path[i + 1]));
            }
            return distances.Sum();
        }
    }
}