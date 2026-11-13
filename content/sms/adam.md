---
title: ADAM · Sequoia Marine Solutions
description: ADAM, Sequoia Marine Solutions' autonomous vertical profiling float: a peristaltic-pump buoyancy engine, Raspberry Pi Pico control and LoRa telemetry.
---

# ADAM page (/sms/adam)

## hero
kicker: Profiling float · 2026
title: ADAM
photo: adam-water

Aquatic Drifter for Autonomous Monitoring. It sinks and rises on its own, logging depth on the way, like the floats that measure the ocean's health.

## spec
kicker: How it works
title: A buoyancy engine in a tube.

A peristaltic pump moves water in and out of an internal bladder, changing ADAM's density so it can complete vertical profiles without a tether.

- Hull: 4 in polycarbonate tube, machined end caps, internal O-rings
- Buoyancy: Peristaltic pump + internal bladder
- Control: Custom PCB, Raspberry Pi Pico (MicroPython)
- Sensing: Blue Robotics depth sensor
- Telemetry: LoRa
- Power: Independent NiMH pack, isolated logic and pump rails

## model
kicker: In 3D
title: Look inside the tube.
alt: 3D model of ADAM, the profiling float: a tall tube with end caps and two recovery handles
size: 1.3 MB

ADAM's float CAD: end caps, recovery handles and the rails that carry the pump and electronics. Drag to orbit, pinch or scroll to zoom.

## poster
photo: poster-adam

## chapters

### Seals that survive the cold
Custom-machined end caps use internal O-rings. The team researched thermal expansion so the seals hold across the temperature swing from a warm deck to cold water.

### Quiet power
An independent NiMH pack with its own regulation feeds the logic and the pump separately, so motor noise doesn't corrupt the depth readings.

### Long-range, low-power radio
A LoRa module sends telemetry to the surface from a custom PCB built around a Raspberry Pi Pico running MicroPython.

## photos
- pool-adam-deploy | tall
- adam-display | tall
- worlds-float-electronics | tall
- worlds-hallway
- worlds-shirt-front
