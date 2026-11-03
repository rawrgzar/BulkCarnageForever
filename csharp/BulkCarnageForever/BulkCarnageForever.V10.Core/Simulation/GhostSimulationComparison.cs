using BulkCarnageForever.V10.Core.Model;

namespace BulkCarnageForever.V10.Core.Simulation;

/// <summary>
/// A detached saved scenario with its current source-meal comparison status.
/// NeedsRecalculation means the recorded meal differs from the saved source fields;
/// changes to weekly settings or catalog nutrition alone do not set this flag.
/// </summary>
public sealed record GhostSimulationComparison(GhostMealSimulation Simulation, bool NeedsRecalculation);
