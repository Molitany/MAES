using System;
using System.Collections.Generic;
using Dora.MapGeneration;
using Dora.Robot;
using Dora.Statistics;
using UnityEngine;

namespace Dora
{
    public class Simulation: MonoBehaviour, ISimulationUnit
    {
        
        public int SimulatedLogicTicks { get; private set; } = 0;
        public int SimulatedPhysicsTicks { get; private set; } = 0;
        public float SimulateTimeSeconds { get; private set; } = 0;
        
        public MapGenerator MapGenerator;
        public RobotSpawner RobotSpawner;
        public ExplorationVisualizer explorationVisualizer;
        
        private SimulationScenario _scenario;
        private SimulationMap<bool> _collisionMap;
        private List<MonaRobot> _robots;

        private ExplorationTracker _explorationTracker;
        private CommunicationManager _communicationManager;

        // Sets up the simulation by generating the map and spawning the robots
        public void SetScenario(SimulationScenario scenario)
        {
            _scenario = scenario;
            _collisionMap = scenario.MapSpawner(MapGenerator);
            _robots = scenario.RobotSpawner(_collisionMap, RobotSpawner);
            _explorationTracker = new ExplorationTracker(_collisionMap, explorationVisualizer);
        }
        
        public void LogicUpdate(SimulationConfiguration config)
        {
            _explorationTracker.LogicUpdate(config, _robots);
            _robots.ForEach(robot => robot.LogicUpdate(config));
            SimulatedLogicTicks++;
        }

        public void PhysicsUpdate(SimulationConfiguration config)
        {
            _robots.ForEach(simUnit => simUnit.PhysicsUpdate(config));
            Physics2D.Simulate(config.PhysicsTickDeltaSeconds);
            SimulateTimeSeconds+= config.PhysicsTickDeltaSeconds;
            SimulatedPhysicsTicks++;
        }
        
        
        // ----- Future work -------
        public object SaveState()
        {
            throw new NotImplementedException();
        }

        public void RestoreState(object stateInfo)
        {
            throw new NotImplementedException();
        }
    }
}