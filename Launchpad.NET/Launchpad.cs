﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading.Tasks;
using Windows.Devices.Enumeration;
using Windows.Devices.Midi;
using Launchpad.NET.Effects;
using Launchpad.NET.Models;
using System.Linq;
using System.Reactive.Subjects;
using System.Threading;

namespace Launchpad.NET
{
    public interface ILaunchpad { }

    public abstract class Launchpad
    {
        protected Dictionary<ILaunchpadEffect, IDisposable> effectsDisposables;
        protected Dictionary<ILaunchpadEffect, Timer> effectsTimers;
        protected List<LaunchpadButton> gridButtons;
        protected MidiInPort inPort;
        protected IMidiOutPort outPort;
        protected List<LaunchpadButton> sideButtons;
        protected List<LaunchpadTopButton> topButtons;
        protected readonly Subject<ILaunchpadButton> whenButtonStateChanged = new Subject<ILaunchpadButton>();

        public ObservableCollection<ILaunchpadEffect> Effects { get; protected set; }
        public string Name { get; set; }
        /// <summary>
        /// Observable event for when a button on the launchpad is pressed or released
        /// </summary>
        public IObservable<ILaunchpadButton> WhenButtonStateChanged => whenButtonStateChanged;

        public Launchpad()
        {
            effectsDisposables = new Dictionary<ILaunchpadEffect, IDisposable>();
            effectsTimers = new Dictionary<ILaunchpadEffect, Timer>();
        }

        /// <summary>
        /// Add an effect to the launchpad
        /// </summary>
        /// <param name="effect"></param>
        /// <param name="updateFrequency"></param>
        public void RegisterEffect(ILaunchpadEffect effect, TimeSpan updateFrequency)
        {
            try
            {

                // Add the effect to the launchpad
                Effects.Add(effect);

                // When the effect is complete, unregister it (add the subscription to a dictionary so we can make sure to release it later)
                effectsDisposables.Add(effect,
                    effect
                        .WhenComplete? // If the effect has implemented whenComplete
                        .Subscribe(UnregisterEffect));

                //if (effect.WhenChangeUpdateFrequency != null)
                //{
                //    // When the effect is complete, unregister it (add the subscription to a dictionary so we can make sure to release it later)
                //    effectsDisposables.Add(effect,
                //        effect
                //            .WhenChangeUpdateFrequency? // If the effect has implemented whenComplete
                //            .Subscribe((newFrequency) => ChangeUpdateFrequency(effect, newFrequency));
                //}

                // Initiate the effect (provide all buttons and button changed event
                effect.Initiate(gridButtons, sideButtons, topButtons, whenButtonStateChanged);

                // Create an update timer at the specified frequency
                effectsTimers.Add(effect, new Timer(state => effect.Update(), null, 0, (int)updateFrequency.TotalMilliseconds));
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
            }

        }
        public abstract void SendMessage(IMidiMessage message);

        public abstract void UnregisterEffect(ILaunchpadEffect effect);
    }

    public static class Novation
    {
        public static async Task<Launchpad> Launchpad(string inputDeviceName = "launchpad", string outputDeviceName = "launchpad")
        {
            try
            {
                // Get all input MIDI devices
                var midiInputDevices = await DeviceInformation.FindAllAsync(MidiInPort.GetDeviceSelector());
                midiInputDevices.ToList().ForEach(device => Debug.WriteLine($"Found input device: {device.Name}"));
                // Get all output MIDI devices
                var midiOutputDevices = await DeviceInformation.FindAllAsync(MidiOutPort.GetDeviceSelector());
                midiInputDevices.ToList().ForEach(device => Debug.WriteLine($"Found output device: {device.Name}"));

                // Find the launchpad input
                foreach (var inputDeviceInfo in midiInputDevices)
                {
                    Debug.WriteLine("INPUT: " + inputDeviceInfo.Name);
                    if (inputDeviceInfo.Name.ToLower().Contains(inputDeviceName.ToLower()))
                    {
                        // Find the launchpad output 
                        foreach (var outputDeviceInfo in midiOutputDevices)
                        {
                            Debug.WriteLine("OUTPUT: " + outputDeviceInfo.Name);
                            // If not a match continue
                            if (!outputDeviceInfo.Name.ToLower().Contains(outputDeviceName.ToLower())) continue;

                            var inPort = await MidiInPort.FromIdAsync(inputDeviceInfo.Id);
                            var outPort = await MidiOutPort.FromIdAsync(outputDeviceInfo.Id);

                            // Return an MK2 if detected
                            if (outputDeviceInfo.Name.ToLower().Contains("mk2"))
                                return new LaunchpadMk2(outputDeviceInfo.Name, inPort, outPort);

                            // Otherwise return Standard
                            return new LaunchpadS(outputDeviceInfo.Name, inPort, outPort);
                        }
                    }
                }

                // Return null if no devices matched the device name provided
                return null;
            }
            catch (Exception e)
            {
                Debug.WriteLine(e);
                return null;
            }
        }
    }
}
