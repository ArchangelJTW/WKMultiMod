using System;

namespace WKMPMod.Core;

/// <summary>
/// SceneManager.LoadScene queues completion. A ready world from BEFORE the
/// request is not the world being joined, even when the scene name is unchanged.
/// </summary>
public sealed class WorldLoadBarrier {
	private readonly int _beforeRevision;
	private readonly bool _requiresSceneLoad;

	public WorldLoadBarrier(int beforeRevision, bool requiresSceneLoad) {
		_beforeRevision = beforeRevision;
		_requiresSceneLoad = requiresSceneLoad;
	}

	public bool IsReady(int currentRevision, bool worldReady) => worldReady
		&& (!_requiresSceneLoad || currentRevision > _beforeRevision);
}

/// <summary>
/// Native saveLoad does not restore LevelId.id: GenerateLevels allocates every
/// ID from WorldLoader.idCounter, which UnloadAllLevels does not reset. Seed the
/// new, isolated generation with the host's target ordinal before generating.
/// </summary>
public static class CheckpointGenerationIdentity {
	public static int CounterStart(int hostOrdinal) {
		// Leave headroom for the bounded snapshot and native procedural tail.
		if (hostOrdinal < 0 || hostOrdinal > int.MaxValue - 4096)
			throw new ArgumentOutOfRangeException(nameof(hostOrdinal), "Invalid checkpoint generation ordinal.");
		return hostOrdinal;
	}

	public static bool MatchesTarget(string expectedLevel, int expectedOrdinal, string? actualLevel, int actualOrdinal) =>
		string.Equals(expectedLevel, actualLevel, StringComparison.Ordinal) && expectedOrdinal == actualOrdinal;
}
