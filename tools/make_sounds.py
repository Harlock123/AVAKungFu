#!/usr/bin/env python3
"""Synthesises AVAKungFu's sound effects and tunes into ../src/AVAKungFu/Sounds as 16-bit mono WAV.

Nothing is sampled from any arcade machine: every effect is built here from square, triangle and
noise voices, and the tunes are new compositions. Run it from anywhere:
    python3 tools/make_sounds.py
"""
import math
import os
import random
import struct
import wave

RATE = 22050
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "AVAKungFu", "Sounds")
RNG = random.Random(1982)


def write(name, samples, volume=0.7):
    peak = max(1e-9, max(abs(s) for s in samples))
    with wave.open(os.path.join(OUT, name), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(b"".join(struct.pack("<h", int(s / peak * volume * 32767)) for s in samples))


def voice(kind, phase):
    p = phase % 1.0
    if kind == "square":
        return 1.0 if p < 0.5 else -1.0
    if kind == "pulse":
        return 1.0 if p < 0.25 else -1.0
    if kind == "tri":
        return 4 * p - 1 if p < 0.5 else 3 - 4 * p
    if kind == "saw":
        return 2 * p - 1
    if kind == "sine":
        return math.sin(2 * math.pi * p)
    return RNG.uniform(-1, 1)


def tone(seconds, f0, f1=None, kind="square", attack=0.004, release=1.0, vibrato=0.0, vib_rate=8.0, gain=1.0):
    """A note gliding from f0 to f1 (exponentially), with a short attack and a shaped decay."""
    n = int(RATE * seconds)
    f1 = f1 or f0
    out, phase, held = [], 0.0, 0.0
    for i in range(n):
        t = i / RATE
        f = f0 * (f1 / f0) ** (i / max(1, n - 1))
        if vibrato:
            f *= 1 + vibrato * math.sin(2 * math.pi * vib_rate * t)
        phase += f / RATE
        if kind == "noise":
            # Noise re-sampled at the voice's rate: a lower 'pitch' sounds rougher.
            if int(phase) != int(phase - f / RATE):
                held = RNG.uniform(-1, 1)
            s = held
        else:
            s = voice(kind, phase)
        env = min(1.0, t / attack) * (1 - i / n) ** release
        out.append(s * env * gain)
    return out


def silence(seconds):
    return [0.0] * int(RATE * seconds)


def cat(*parts):
    out = []
    for p in parts:
        out.extend(p)
    return out


def mix(*parts):
    n = max(len(p) for p in parts)
    out = [0.0] * n
    for p in parts:
        for i, v in enumerate(p):
            out[i] += v
    return out


NOTES = {"C": 0, "D": 2, "E": 4, "F": 5, "G": 7, "A": 9, "B": 11}


def hz(note):
    n = NOTES[note[0]]
    rest = note[1:]
    while rest and rest[0] in "#b":
        n += 1 if rest[0] == "#" else -1
        rest = rest[1:]
    return 440.0 * 2 ** ((12 * (int(rest) + 1) + n - 69) / 12)


def tune(bpm, lead, bass, kind="pulse"):
    """Two voices: a lead and a bass line, as (note, beats) lists; '-' is a rest."""
    beat = 60.0 / bpm

    def line(notes, k, gain, release):
        out = []
        for note, beats in notes:
            d = beats * beat
            out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=k, release=release, gain=gain))
        return out

    return mix(line(lead, kind, 0.55, 0.6), line(bass, "tri", 0.6, 0.3))


def seq(bpm, notes, kind="pulse", gain=0.55, release=0.6):
    beat = 60.0 / bpm
    out = []
    for note, beats in notes:
        d = beats * beat
        out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=kind, release=release, gain=gain))
    return out


def main():
    os.makedirs(OUT, exist_ok=True)
    for f in os.listdir(OUT):
        if f.endswith(".wav"):
            os.remove(os.path.join(OUT, f))
    # Blows and cries.
    write("Punch.wav", mix(tone(0.07, 900, 300, "noise", attack=0.003, release=1.2), tone(0.06, 260, 120, "square", release=1.2, gain=0.4)), 0.35)
    write("Kick.wav", mix(tone(0.11, 700, 180, "noise", attack=0.003, release=1.0), tone(0.09, 200, 90, "square", release=1.0, gain=0.4)), 0.4)
    write("Swish.wav", tone(0.16, 2400, 600, "noise", attack=0.02, release=1.0), 0.25)
    write("Hit.wav", mix(tone(0.14, 180, 70, "square", release=0.8), tone(0.12, 1400, 300, "noise", release=1.0, gain=0.6)), 0.5)
    write("BossHit.wav", mix(tone(0.2, 140, 50, "square", release=0.7), tone(0.18, 900, 200, "noise", release=0.9, gain=0.6)), 0.55)
    write("Hurt.wav", cat(tone(0.08, 520, 400, "square", release=0.6), tone(0.16, 380, 220, "square", release=0.6)), 0.4)
    write("Grab.wav", tone(0.18, 300, 150, "saw", release=0.6), 0.35)
    write("Shake.wav", tone(0.05, 600, 500, "tri", release=1.0), 0.25)
    write("Throw.wav", tone(0.2, 1800, 900, "noise", attack=0.01, release=0.8), 0.3)
    write("Jar.wav", mix(tone(0.35, 2600, 800, "noise", release=0.8), *[tone(0.25, f, kind="tri", release=1.4, gain=0.3) for f in [1800, 2300, 2900]]), 0.45)
    write("Hiss.wav", tone(0.4, 5000, 3000, "noise", attack=0.05, release=0.5), 0.25)
    write("Teleport.wav", cat(tone(0.18, 300, 1800, "sine", release=0.3, vibrato=0.08, vib_rate=25), tone(0.12, 1800, 600, "sine", release=0.5)), 0.35)
    write("BossDown.wav", cat(tone(0.3, 220, 80, "square", release=0.6), tone(0.6, 400, 40, "noise", release=0.6)), 0.55)
    write("Death.wav", cat(*[tone(0.16, hz(n), kind="tri", release=0.4) for n in ["A4", "G4", "F4", "E4"]], tone(0.6, hz("A3"), kind="tri", release=0.5)), 0.45)
    write("FloorStart.wav", cat(*[tone(0.12, hz(n), kind="pulse", release=0.4) for n in ["D5", "E5", "G5", "A5"]], tone(0.4, hz("D6"), kind="pulse", release=0.7)), 0.38)
    write("FloorClear.wav", tune(170, [("D5", 0.5), ("E5", 0.5), ("G5", 0.5), ("A5", 0.5), ("D6", 0.5), ("A5", 0.5), ("D6", 2)], [("D3", 1), ("A2", 1), ("D3", 2)], kind="pulse"), 0.45)
    write("Rescue.wav", tune(120, [("D5", 1), ("E5", 0.5), ("G5", 0.5), ("A5", 1), ("B5", 0.5), ("A5", 0.5), ("G5", 1), ("E5", 1), ("D5", 3)],
                            [("D3", 2), ("G2", 2), ("A2", 2), ("D3", 3)], kind="tri"), 0.45)
    write("ExtraLife.wav", cat(*[tone(0.08, hz(n), kind="pulse", release=0.3) for n in ["D6", "E6", "G6", "A6", "D7"]]), 0.4)

    # The temple: a pentatonic theme in D over a driving drum-and-bass beat (new). Loops.
    beat = 60.0 / 140
    drums = []
    for bar in range(8):
        for k in range(4):
            drums.extend(mix(tone(beat * 0.5, 110, 50, "sine", release=0.8), tone(beat * 0.5, 200, 120, "noise", release=1.5, gain=0.25)) if k % 2 == 0
                         else mix(silence(beat * 0.5), tone(beat * 0.5, 3000, 1500, "noise", release=2.0, gain=0.3)))
            drums.extend(tone(beat * 0.5, 4000, 2500, "noise", release=3.0, gain=0.15))
    bass_notes = []
    for root in ["D2", "D2", "A1", "C2", "D2", "D2", "G1", "A1"]:
        bass_notes += [(root, 0.5), (root, 0.5), ("-", 0.5), (root, 0.5), (root, 0.5), ("-", 0.5), (root, 1)]
    lead = [("D5", 1), ("E5", 0.5), ("G5", 0.5), ("A5", 1), ("G5", 0.5), ("E5", 0.5), ("D5", 2), ("C5", 1), ("D5", 1),
            ("A5", 1), ("C6", 0.5), ("A5", 0.5), ("G5", 1), ("E5", 1), ("G5", 1), ("A5", 1), ("D5", 2),
            ("E5", 1), ("G5", 1), ("A5", 0.5), ("G5", 0.5), ("E5", 1), ("D5", 2), ("C5", 1), ("A4", 1), ("D5", 2), ("-", 2)]
    bass = seq(140, bass_notes, kind="tri", gain=0.5, release=0.5)
    top = seq(140, lead, kind="pulse", gain=0.26, release=0.5)
    write("Music.wav", mix(bass, top, drums), 0.42)

    write("TitleTune.wav", tune(100,
                                [("D5", 1), ("E5", 0.5), ("G5", 0.5), ("A5", 2), ("C6", 1), ("A5", 0.5), ("G5", 0.5), ("E5", 2),
                                 ("G5", 1), ("A5", 0.5), ("G5", 0.5), ("E5", 1), ("D5", 1), ("C5", 1), ("A4", 1), ("D5", 2)],
                                [("D3", 2), ("D3", 2), ("C3", 2), ("A2", 2), ("G2", 2), ("A2", 2), ("D3", 4)], kind="tri"), 0.45)
    write("StartTune.wav", tune(160, [("D5", 0.5), ("E5", 0.5), ("G5", 0.5), ("A5", 1), ("G5", 0.5), ("D6", 2)], [("D3", 2), ("A2", 1), ("D3", 2)], kind="pulse"), 0.45)
    write("GameOver.wav", tune(80, [("A5", 1), ("G5", 1), ("E5", 1), ("C5", 1), ("D5", 3)], [("D4", 2), ("C4", 2), ("A3", 1), ("D3", 3)], kind="tri"), 0.45)

if __name__ == "__main__":
    main()
