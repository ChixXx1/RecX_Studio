using System;

namespace RecX_Studio.Models
{
    public class RecordingSession
    {
        public string OutputPath { get; set; }
        public DateTime StartTime { get; set; }
        public TimeSpan Duration { get; set; }
        public MediaSource Source { get; set; }
        public bool IsRecording { get; set; }
    }
}