﻿using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Random = System.Random;

namespace Maes.Map.MapGen
{
    public class BuildingGenerator : MapGenerator
    {
        private BuildingMapConfig _config;
        private float _wallHeight;

        public void Init(BuildingMapConfig config, float wallHeight = 2.0f)
        {
            _config = config;
            _wallHeight = wallHeight;
        }

        /// TODO: Update this to new format
        /// <summary>
        /// Generates a building map using the unity game objects Plane, InnerWalls and WallRoof.
        /// </summary>
        /// <param name="config">Determines how the map is generated</param>
        /// <param name="wallHeight">A lower height can make it easier to see the robots. Must be a positive value.</param>
        /// <returns> A SimulationMap represents a map of square tiles, where each tile is divided into 8 triangles as
        /// used in the Marching Squares Algorithm.</returns>
        public SimulationMap<bool> GenerateBuildingMap()
        {
            // Clear and destroy objects from previous map
            ClearMap();

            var random = new Random(_config.randomSeed);

            var emptyMap = GenerateEmptyMap(_config.bitMapWidth, _config.bitMapHeight);

            var mapWithHalls = GenerateMapWithHalls(emptyMap, _config, random);

            var mapWithWallsAroundRooms = AddWallAroundRoomRegions(mapWithHalls);

            var mapWithRooms = GenerateRoomsBetweenHalls(mapWithWallsAroundRooms, _config, random);

            var closedHallwayMap = CloseOffHallwayEnds(mapWithRooms);

            // Rooms and halls sorted according to biggest hall first. Biggest hall set to main room.
            // If both rooms are halls, sort according to size
            var rooms = GetSortedRooms(closedHallwayMap);

            var connectedMap = ConnectRoomsWithDoors(rooms, closedHallwayMap, random, _config);

            var borderedMap = CreateBorderedMap(connectedMap, _config.bitMapWidth, _config.bitMapHeight, _config.borderSize);

            // For debugging
            // mapToDraw = borderedMap;

            // The rooms should now reflect their relative shifted positions after adding borders round map.
            rooms.ForEach(r => r.OffsetCoordsBy(_config.borderSize, _config.borderSize));
            var meshGen = GetComponent<MeshGenerator>();
            var collisionMap = meshGen.GenerateMesh(borderedMap.Clone() as Tile[,], _wallHeight, true,
                rooms);

            // Rotate to fit 2D view
            Plane.rotation = Quaternion.AngleAxis(-90, Vector3.right);

            ResizePlaneToFitMap(_config.bitMapHeight, _config.bitMapWidth);

            MovePlaneAndWallRoofToFitWallHeight(_wallHeight);

            return collisionMap;
        }

        private static Tile[,] ConnectRoomsWithDoors(List<Room> sortedRooms, Tile[,] oldMap, Random random,
            BuildingMapConfig config)
        {
            var connectedMap = oldMap.Clone() as Tile[,];
            // Whether the given room is connected to the main room
            var connectedRooms = new List<Room>();
            var nonConnectedRooms = new List<Room>();

            foreach (var room in sortedRooms)
            {
                if (room.isAccessibleFromMainRoom)
                    connectedRooms.Add(room);
                else
                    nonConnectedRooms.Add(room);
            }

            if (nonConnectedRooms.Count == 0)
                return connectedMap;

            foreach (var connectedRoom in connectedRooms)
            {
                foreach (var nonConnectedRoom in nonConnectedRooms)
                {
                    if (connectedRoom == nonConnectedRoom || connectedRoom.IsConnected(nonConnectedRoom))
                        continue;

                    // If they share any wall, they must be adjacent
                    var sharedWallTiles = connectedRoom.GetSharedWallTiles(nonConnectedRoom);
                    if (sharedWallTiles.Count <= 0) 
                        continue;

                    // The maxima of x and y are needed to isolate the coordinates for each line of wall
                    var smallestXValue = sharedWallTiles.Min(tile => tile.x);
                    var biggestXValue = sharedWallTiles.Max(tile => tile.x);
                    var smallestYValue = sharedWallTiles.Min(tile => tile.y);
                    var biggestYValue = sharedWallTiles.Max(tile => tile.y);

                    var maxDoors = random.Next(1, 4);
                    if (smallestYValue == biggestYValue || smallestXValue == biggestXValue)
                        maxDoors = 1;
                    var doorsMade = 0;
                    var doorPadding = config.doorPadding;

                    if (doorsMade >= maxDoors)
                        continue;

                    // A shared wall with the smallest y value
                    // A shared line/wall in the bottom
                    var line = sharedWallTiles.FindAll(c => c.y == smallestYValue).ToList();
                    if (line.Count > config.doorWidth + (doorPadding * 2))
                    {
                        line.Sort((c1, c2) => c1.x - c2.x); // Ascending order
                        var doorStartingIndex = random.Next(0 + doorPadding,
                            (line.Count - 1) - (int)config.doorWidth - doorPadding);
                        var doorStartingPoint = line[doorStartingIndex];
                        for (var i = 0; i < config.doorWidth; i++)
                        {
                            connectedMap![doorStartingPoint.x + i, doorStartingPoint.y] = new Tile(TileType.Room);
                        }

                        Room.ConnectRooms(connectedRoom, nonConnectedRoom);
                        if (++doorsMade >= maxDoors)
                            continue;
                    }

                    // Shared wall with the biggest y value
                    // A shared line/wall in the top
                    line = sharedWallTiles.FindAll(c => c.y == biggestYValue).ToList();
                    if (line.Count > config.doorWidth + (doorPadding * 2))
                    {
                        line.Sort((c1, c2) => c1.x - c2.x); // Ascending order
                        var doorStartingIndex = random.Next(0 + doorPadding,
                            (line.Count - 1) - (int)config.doorWidth - doorPadding);
                        var doorStartingPoint = line[doorStartingIndex];
                        for (var i = 0; i < config.doorWidth; i++)
                        {
                            connectedMap![doorStartingPoint.x + i, doorStartingPoint.y] = new Tile(TileType.Room);
                        }

                        Room.ConnectRooms(connectedRoom, nonConnectedRoom);
                        if (++doorsMade >= maxDoors)
                            continue;
                    }

                    // A shared wall with the smallest x value
                    // A shared line/wall on the left
                    line = sharedWallTiles.FindAll(c => c.x == smallestXValue).ToList();
                    if (line.Count > config.doorWidth + (doorPadding * 2))
                    {
                        line.Sort((c1, c2) => c1.y - c2.y); // Ascending order
                        var doorStartingIndex = random.Next(0 + doorPadding,
                            (line.Count - 1) - (int)config.doorWidth - doorPadding);
                        var doorStartingPoint = line[doorStartingIndex];
                        for (var i = 0; i < config.doorWidth; i++)
                        {
                            connectedMap![doorStartingPoint.x, doorStartingPoint.y + i] = new Tile(TileType.Room);
                        }

                        Room.ConnectRooms(connectedRoom, nonConnectedRoom);
                        if (++doorsMade >= maxDoors)
                            continue;
                    }

                    // A shared wall with the biggest x value
                    // A shared line/wall on the right
                    line = sharedWallTiles.FindAll(c => c.x == biggestXValue).ToList();
                    if (line.Count > config.doorWidth + (doorPadding * 2))
                    {
                        line.Sort((c1, c2) => c1.y - c2.y); // Ascending order
                        var doorStartingIndex = random.Next(0 + doorPadding,
                            (line.Count - 1) - (int)config.doorWidth - doorPadding);
                        var doorStartingPoint = line[doorStartingIndex];
                        for (var i = 0; i < config.doorWidth; i++)
                        {
                            connectedMap![doorStartingPoint.x, doorStartingPoint.y + i] = new Tile(TileType.Room);
                        }

                        Room.ConnectRooms(connectedRoom, nonConnectedRoom);
                    }
                }
            }

            // Recursive call that continues, until all rooms and halls are connected
            connectedMap = ConnectRoomsWithDoors(sortedRooms, connectedMap, random, config);

            return connectedMap;
        }

        private List<Room> GetSortedRooms(Tile[,] map)
        {
            var roomRegions = GetRegions(map, TileType.Room, TileType.Hall);

            var rooms = roomRegions.Select(region => new Room(region, map)).ToList();

            // Sort by first tiletype = Hall, then size
            // Descending order. Hallway > other room types
            rooms.Sort((o1, o2) => {
                var o1x = o1.tiles[0].x;
                var o1y = o1.tiles[0].y;
                var o2x = o2.tiles[0].x;
                var o2y = o2.tiles[0].y;
                
                if (map[o1x, o1y] == map[o2x, o2y]) 
                    return o2.roomSize - o1.roomSize;
                if (map[o1x, o1y].Type == TileType.Hall && map[o2x, o2y].Type != TileType.Hall)
                    return -1;
                if (map[o1x, o1y].Type != TileType.Hall && map[o2x, o2y].Type == TileType.Hall)
                    return 1;

                return o2.roomSize - o1.roomSize;
            });
            // This should now be the biggest hallway
            rooms[0].isMainRoom = true;
            rooms[0].isAccessibleFromMainRoom = true;

            return rooms;
        }

        private static Tile[,] CloseOffHallwayEnds(Tile[,] oldMap)
        {
            var map = oldMap.Clone() as Tile[,];

            var mapWidth = map!.GetLength(0);
            var mapHeight = map.GetLength(1);

            for (var x = 0; x < mapWidth; x++)
            {
                for (var y = 0; y < mapHeight; y++)
                {
                    if (x == mapWidth - 1 || x == 0 || y == 0 || y == mapHeight - 1)
                    {
                        map[x, y] = new Tile(TileType.Wall);
                    }
                }
            }

            return map;
        }

        private Tile[,] GenerateRoomsBetweenHalls(Tile[,] oldMap, BuildingMapConfig config, Random random)
        {
            var mapWithRooms = oldMap.Clone() as Tile[,];

            var roomRegions = GetRegions(mapWithRooms, TileType.Room);

            foreach (var roomRegion in roomRegions)
            {
                SplitRoomRegion(mapWithRooms, roomRegion, config, false, true, random);
            }

            return mapWithRooms;
        }

        // This function has side effects on the map!
        private void SplitRoomRegion(Tile[,] map, List<Vector2Int> roomRegion, BuildingMapConfig config,
            bool splitOnXAxis, bool forceSplit, Random random)
        {
            // Check if we want to split. This allows for different size rooms
            var shouldSplit = random.Next(0, 100) <= config.roomSplitChancePercent;

            if (!shouldSplit && !forceSplit)
                return;

            // Find where to split
            // Rotate 90 degrees every time an room is split
            // We either use the x axis or y axis as starting point
            var coordinates = splitOnXAxis ? roomRegion.Select(coordinate => coordinate.x).ToList() : roomRegion.Select(coordinate => coordinate.y).ToList();

            // Filter out coords that would be too close to the edges to allow for 2 rooms side by side according to config.minRoomSideLength
            var smallestValue = coordinates.Max();
            var biggestValue = coordinates.Min();

            // +1 to allow for wall between room spaces
            var filteredCoordinates = coordinates.FindAll(x =>
                x + config.minRoomSideLength < biggestValue - config.minRoomSideLength && // Right side
                x > smallestValue + config.minRoomSideLength
            );

            // If no possible split on the current axis is possible, try the other one with guaranteed split
            if (filteredCoordinates.Count == 0)
            {
                // Force split only gets once chance to avoid stack overflow
                if (!forceSplit)
                {
                    SplitRoomRegion(map, roomRegion, config, !splitOnXAxis, true, random);
                }

                return;
            }

            // Select random index to start the wall
            var selectedIndex = random.Next(filteredCoordinates.Count);
            var wallStartCoordinate = filteredCoordinates[selectedIndex];

            // Create wall
            if (splitOnXAxis)
            {
                foreach (var c in roomRegion.Where(c => c.x == wallStartCoordinate))
                {
                    map[c.x, c.y] = new Tile(TileType.Wall);
                }
            }
            else
            {
                foreach (var c in roomRegion.Where(c => c.y == wallStartCoordinate))
                {
                    map[c.x, c.y] = new Tile(TileType.Wall);
                }
            }

            // Get two new regions
            Vector2Int region1Tile, region2Tile; // A random tile from each region. We use flooding algorithm to find the rest
            if (splitOnXAxis)
            {
                region1Tile = roomRegion.Find(c => c.x < wallStartCoordinate);
                region2Tile = roomRegion.Find(c => c.x > wallStartCoordinate);
            }
            else
            {
                region1Tile = roomRegion.Find(c => c.y < wallStartCoordinate);
                region2Tile = roomRegion.Find(c => c.y > wallStartCoordinate);
            }

            var newRegion1 = GetRegionTiles(region1Tile.x, region1Tile.y, map);
            var newRegion2 = GetRegionTiles(region2Tile.x, region2Tile.y, map);

            // Run function recursively
            SplitRoomRegion(map, newRegion1, config, !splitOnXAxis, false, random);
            SplitRoomRegion(map, newRegion2, config, !splitOnXAxis, false, random);
        }

        private Tile[,] AddWallAroundRoomRegions(Tile[,] oldMap)
        {
            var mapWithRoomWalls = oldMap.Clone() as Tile[,];
            var roomRegions = GetRegions(mapWithRoomWalls, TileType.Room);

            foreach (var region in roomRegions)
            {
                // Get smallest and largest x and y
                var smallestX = region.Select(coordinate => coordinate.x).Min();
                var biggestX = region.Select(coordinate => coordinate.x).Max();
                var smallestY = region.Select(coordinate => coordinate.y).Min();
                var biggestY = region.Select(coordinate => coordinate.y).Max();

                foreach (var coordinate in region.Where(coordinate => coordinate.x == smallestX || 
                                                                      coordinate.x == biggestX || 
                                                                      coordinate.y == smallestY || 
                                                                      coordinate.y == biggestY))
                {
                    mapWithRoomWalls![coordinate.x, coordinate.y] = new Tile(TileType.Wall);
                }
            }

            return mapWithRoomWalls;
        }

        private Tile[,] GenerateMapWithHalls(Tile[,] map, BuildingMapConfig config, Random random)
        {
            var mapWithHalls = map.Clone() as Tile[,];

            // Rotate 90 degrees every time a hallway is generated
            var usingXAxis = true;
            while (GetHallPercentage(mapWithHalls) < config.maxHallInPercent)
            {
                // We always split the currently biggest room
                var roomRegions = GetRegions(mapWithHalls, TileType.Room);
                var biggestRoom = roomRegions.OrderByDescending(l => l.Count).ToList()[0];

                // We either use the x axis or y axis as starting point
                var coordinates = usingXAxis ? biggestRoom.Select(coordinate => coordinate.x).ToList() : biggestRoom.Select(coordinate => coordinate.y).ToList();

                // Filter out coords too close to the edges to be a hall
                var sortedCoords = coordinates.OrderBy(c => c).ToList();
                var smallestValue = sortedCoords[0];
                var biggestValue = sortedCoords[^1];

                // Plus 2 to allow for inner walls in the room spaces
                var filteredCoords = 
                    sortedCoords.FindAll(x => x + config.minRoomSideLength + 2 < biggestValue - config.minRoomSideLength + 2 && // Right side
                                              x > smallestValue + config.minRoomSideLength + 2
                );

                if (filteredCoords.Count == 0)
                {
                    Debug.Log("The maxHallInPercent of " + config.maxHallInPercent + " could not be achieved.");
                    break;
                }

                // Select random index to fill with hallway
                var selectedIndex = random.Next(filteredCoords.Count);
                var hallStartCoordinate = filteredCoords[selectedIndex];

                if (usingXAxis)
                {
                    // Fill with hall tiles
                    foreach (var c in biggestRoom.Where(c => hallStartCoordinate <= c.x && c.x < hallStartCoordinate + config.hallWidth))
                    {
                        mapWithHalls![c.x, c.y] = new Tile(TileType.Hall);
                    }

                    usingXAxis = false;
                }
                else
                {
                    // Fill with hall tiles
                    foreach (var c in biggestRoom.Where(c => hallStartCoordinate <= c.y && c.y < hallStartCoordinate + config.hallWidth))
                    {
                        mapWithHalls![c.x, c.y] = new Tile(TileType.Hall);
                    }

                    usingXAxis = true;
                }
            }

            return mapWithHalls;
        }

        private static Tile[,] GenerateEmptyMap(int width, int height)
        {
            var emptyMap = new Tile[width, height];

            for (var x = 0; x < emptyMap.GetLength(0); x++)
            {
                for (var y = 0; y < emptyMap.GetLength(1); y++)
                {
                    emptyMap[x, y] = new Tile(TileType.Room);
                }
            }

            return emptyMap;
        }

        private static float GetHallPercentage(Tile[,] map)
        {
            var width = map.GetLength(0);
            var height = map.GetLength(1);
            var mapSize = width * height;

            var amountOfHall = 0;
            for (var x = 0; x < width; x++)
            {
                for (var y = 0; y < height; y++)
                {
                    amountOfHall += map[x, y].Type == TileType.Hall ? 1 : 0;
                }
            }

            return (amountOfHall / (float)mapSize) * 100f;
        }

    }
}