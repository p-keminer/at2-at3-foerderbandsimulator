using System;
using System.Collections.Generic;
using System.Linq;

namespace Plant.Core
{
    public sealed class SampledHallCounter
    {
        private bool previous;
        public long Rising { get; private set; }
        public long Signed { get; private set; }
        public SampledHallCounter(bool initialLevel) { previous = initialLevel; }
        public bool Sample(SensorFrame input, ActuatorFrame previousOutput)
        {
            if (!input.Valid) return false;
            bool rising = input.HallHigh && !previous;
            previous = input.HallHigh;
            if (rising)
            {
                Rising++;
                Signed += previousOutput.MotorRight == previousOutput.MotorLeft ? 0 : previousOutput.MotorRight ? 1 : -1;
            }
            return rising;
        }
        public void ResetCount() { Rising = 0; Signed = 0; } // Preserve edge memory.
    }

    public sealed class ReferenceController
    {
        private readonly bool leftActiveHigh, opticalActiveHigh, rightActiveHigh;
        private bool previousHall, previousOptical, initialized, armed = true;
        private long observed, testCount, count, cycleStart, sequence, previousTime;
        private double? result;
        private bool windowSeen, windowClosed;
        private ActuatorFrame output = new();
        private CycleState state;
        private MachineMode machineMode = MachineMode.Automatic;
        private string message = "Eigene Basissteuerung / Nachbildung";
        public Exercise Exercise { get; set; }
        public bool Stopped { get; private set; }
        public bool JogLeft { get; set; }
        public bool JogRight { get; set; }
        public bool UseSpeedSelection { get; set; }
        public double SpeedVoltage { get; set; } = 5;
        private double? calibration;
        public double? Calibration
        {
            get => calibration;
            set { Numbers.Require(value == null || (Numbers.Finite(value.Value) && value > 0), "Kalibrierung muss positiv und endlich sein."); calibration = value; }
        }
        // Deliberately only polarities. No PlantModel, geometry, encoder pitch or diagnostics.
        public ReferenceController(bool leftActiveHigh, bool opticalActiveHigh, bool rightActiveHigh, Exercise exercise)
        {
            this.leftActiveHigh = leftActiveHigh; this.opticalActiveHigh = opticalActiveHigh;
            this.rightActiveHigh = rightActiveHigh; Exercise = exercise;
        }
        public void InitializeLevel(SensorFrame input) { previousHall = input.HallHigh; initialized = true; }
        public void ToggleStop() { Stopped = !Stopped; JogLeft = JogRight = false; }
        public void ChangeMachineMode(MachineMode value)
        {
            if (machineMode == value) return;
            machineMode = value; Stopped = true; JogLeft = JogRight = false;
            if (Active) { state = CycleState.Faulted; message = "Betriebsartwechsel: laufender Messzyklus verworfen. Messung zurücksetzen."; }
        }
        private bool Active => state == CycleState.Transport || state == CycleState.Measuring || state == CycleState.AfterMeasurement;
        public void ResetMeasurement()
        {
            state = CycleState.Ready; count = 0; windowSeen = windowClosed = false;
            previousOptical = false; armed = true;
            message = "Neue Messung freigegeben; letztes Ergebnis bleibt erhalten.";
        }
        public void ResetTestCounter() { testCount = 0; }
        public ControllerTelemetry Telemetry => new(state, Stopped, machineMode, observed, testCount, count, result,
            state == CycleState.Measuring, calibration == null ? message + " / Kalibrierung fehlt" : message);
        public ActuatorFrame Tick(SensorFrame input)
        {
            Numbers.Require(Numbers.Finite(SpeedVoltage) && SpeedVoltage >= 0 && SpeedVoltage <= 10, "Drehzahlvorgabe ungültig.");
            if (!input.Valid)
            {
                state = CycleState.Faulted; message = "Eingänge ungültig.";
                return output = new(Valid: false, Sequence: ++sequence, TimeUs: input.TimeUs);
            }
            if (!initialized) InitializeLevel(input);
            bool hallRise = input.HallHigh && !previousHall;
            previousHall = input.HallHigh;
            if (hallRise)
            {
                observed++;
                testCount += output.MotorRight == output.MotorLeft ? 0 : output.MotorRight ? 1 : -1;
            }
            bool l = input.SensorLeftHigh == leftActiveHigh;
            bool o = input.OpticalHigh == opticalActiveHigh;
            bool r = input.SensorRightHigh == rightActiveHigh;
            if (Stopped && Active) cycleStart += input.TimeUs - previousTime;
            previousTime = input.TimeUs;
            if (!l) armed = true;
            bool rightCommand = false, leftCommand = false;
            if (machineMode == MachineMode.Automatic)
            {
                if (!Stopped && (state == CycleState.Ready || state == CycleState.Complete) && l && armed)
                {
                    state = CycleState.Transport; count = 0; windowSeen = windowClosed = false;
                    cycleStart = input.TimeUs; armed = false; message = "Messzyklus läuft.";
                }
                // Atomic sample order: decode optical window, reset on entry, count Hall rise,
                // close on optical LOW, then evaluate end sensor. Entry includes simultaneous rise.
                if (Active && o && !previousOptical)
                {
                    state = CycleState.Measuring; count = 0; windowSeen = true;
                    result = null; message = "Optisches Messfenster aktiv.";
                }
                if (state == CycleState.Measuring && o && hallRise) count++;
                if (state == CycleState.Measuring && !o && previousOptical)
                {
                    windowClosed = true; state = CycleState.AfterMeasurement;
                    result = calibration.HasValue ? count * calibration.Value : null;
                    message = "Messwert aus abgetasteten Hall-Flanken berechnet.";
                }
                if (Active && r)
                {
                    state = windowSeen && windowClosed ? CycleState.Complete : CycleState.Faulted;
                    message = state == CycleState.Complete ? "Messzyklus beendet am Sensor 1." : "Ungültige Messung: optisches Fenster fehlt oder ist nicht geschlossen.";
                }
                if (Active && input.TimeUs - cycleStart > 120000000)
                { state = CycleState.Faulted; message = "Zeitüberschreitung: Sensoren, Material oder Blockade prüfen."; }
                rightCommand = Active && !Stopped && !r;
            }
            else
            {
                // Sensors never start transport in Hand. End limits remain active in controller.
                rightCommand = !Stopped && JogRight && !JogLeft && !r;
                leftCommand = !Stopped && JogLeft && !JogRight && !l;
            }
            previousOptical = o;
            bool measuring = state == CycleState.Measuring;
            bool movingCommand = rightCommand || leftCommand;
            double volts = UseSpeedSelection && machineMode == MachineMode.Automatic ? (measuring ? 3.5 : 6) : SpeedVoltage;
            bool red = Exercise == Exercise.AT2 ? Active : movingCommand;
            return output = new(rightCommand, leftCommand, Analog.OutputRaw(volts), red,
                measuring && Active, !Active && state != CycleState.Faulted && !Stopped,
                true, ++sequence, input.TimeUs);
        }
    }

    public sealed record CalibrationRun(double DistanceMm, long SampledPulses, double MmPerPulse);
    public sealed class CalibrationBook
    {
        private readonly List<CalibrationRun> runs = new();
        public IReadOnlyList<CalibrationRun> Runs => runs.AsReadOnly();
        public double? Mean => runs.Count == 3 ? runs.Average(r => r.MmPerPulse) : null;
        public void Add(double measuredDistanceMm, long observedPulses)
        {
            Numbers.Require(Numbers.Finite(measuredDistanceMm) && measuredDistanceMm > 0 && observedPulses > 0, "Kalibrierfahrt benötigt gemessene Strecke > 0 und erfasste Impulse > 0.");
            Numbers.Require(runs.Count < 3, "Drei Fahrten vorhanden. Zuerst Kalibrierliste leeren.");
            runs.Add(new(measuredDistanceMm, observedPulses, measuredDistanceMm / observedPulses));
        }
        public void Clear() => runs.Clear();
    }
}
