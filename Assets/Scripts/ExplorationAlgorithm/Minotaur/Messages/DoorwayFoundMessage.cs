﻿using System;
using System.Collections.Generic;
using Maes.Map;
using UnityEngine;

namespace Maes.ExplorationAlgorithm.Minotaur
{
    public partial class MinotaurAlgorithm : IExplorationAlgorithm
    {
        public class DoorwayFoundMessage : IMinotaurMessage
        {
            private readonly Doorway _doorway;

            private readonly int _requesterID;

            public DoorwayFoundMessage(Doorway doorway, int requesterID)
            {
                _doorway = doorway;
                _requesterID = requesterID;
            }

            public IMinotaurMessage Combine(IMinotaurMessage otherMessage, MinotaurAlgorithm minotaur)
            {
                return this;
            }

            /// <summary>
            /// Process DoorwayFoundMessage, bidding on entering the doorway depending on the length of its path to it
            /// returns null it has to pass doorways to do so
            /// </summary>
            public IMinotaurMessage? Process(MinotaurAlgorithm minotaur)
            {
                if (!minotaur._doorways.Contains(_doorway))
                {
                    minotaur._doorways.Add(_doorway);
                }

                var doorwayTile = minotaur._map.FromSlamMapCoordinate(_doorway.Center);
                var pathLengthToDoorway = minotaur._map.GetPath(doorwayTile, false, false);
                if (pathLengthToDoorway != null)
                {
                    foreach (Doorway knownDoorway in minotaur._doorways)
                    {
                        if (pathLengthToDoorway.Contains(minotaur._map.FromSlamMapCoordinate(knownDoorway.Center)))
                        {
                            return null;
                        }
                    }
                    var bid = new Dictionary<int, int>() { { minotaur._controller.GetRobotID(), pathLengthToDoorway.Count } };
                    return new BiddingMessage(_requesterID, bid, _doorway);
                }
                return null;
            }
        }
    }
}