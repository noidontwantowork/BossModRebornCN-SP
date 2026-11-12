namespace BossMod;

// remembers what the user had open in the replay window, so it can be restored after a plugin reload
public record struct ReplayMemory(string Path, bool IsOpen, DateTime PlaybackPosition);
