# 🚀 LaserOne

<b>NOT YET FULLY FUNCTIONAL , ANY HELP TO IMPROVE THIS APP VERY APRECIATED.</b>


<b>GOAL:</b>



<p align="center">
  <b>A modern, lightweight, and open-source CAM and control software for GRBL-based laser and CNC machines.</b>
</p>

<p align="center">
  <img src="https://img.shields.io/badge/C%23-.NET-purple.svg" alt=".NET">
  <img src="https://img.shields.io/badge/UI-Avalonia-blue.svg" alt="Avalonia UI">
  <img src="https://img.shields.io/badge/Firmware-GRBL-orange.svg" alt="GRBL">
  <img src="https://img.shields.io/badge/Status-v0.7%20(Milestone%207)-success.svg" alt="Status">
  <img src="https://img.shields.io/badge/License-MIT-green.svg" alt="License">
</p>

---

**LaserOne** was created with a clear goal: to provide a modern, smooth, and cross-platform desktop alternative for laser engraving and cutting design and control (tested and optimized for machines like *Sculpfun*). 

Written entirely in **C#** utilizing the power of **Avalonia UI**, LaserOne combines a responsive 2D CAD environment with a real-time machine control terminal.

---

## ✨ Key Features

*   🎨 **2D Vector CAD Canvas:** 
    *   Smooth navigation with full support for Zoom (mouse wheel) and Pan (middle mouse button / Alt key).
    *   Dynamic rulers and millimeter grid with *Grid Snap* functionality.
    *   Single selection, multiple *Box Selection*, and keyboard shortcuts (`Delete`, `Esc`).
*   ✏️ **Geometric Drawing Tools:**
    *   Creation and interactive manipulation of **Rectangles, Lines, and Circles**.
    *   Real-time properties panel (millimeter-precise coordinate and size editing).
    *   Visual handle system for rotation and scaling transformation.
*   ⚙️ **Integrated G-Code Generator:**
    *   Automatic translation of vectors into standard G-Code instructions (`G0`, `G1`, `M3/M5`) with laser speed and power parameter management.
*   🔌 **Serial Connectivity & Advanced Terminal:**
    *   Automatic COM port detection.
    *   Integrated G-Code console for sending manual commands and viewing real-time board response logs.
    *   Asynchronous command streaming to the machine.
*   🕹️ **CNC Control Panel (3x3 Grid):**
    *   Quick commands for Homing (`$H`), workpiece origin setting (`G92 X0 Y0`), Emergency Stop (Soft Reset), Pause (`!`), and Resume (`~`).
    *   **Frame** feature: automatic bounding box calculation to preview the engraving area before running the job.

---

## 🛠️ Tech Stack

*   **Language:** C# (.NET 8+)
*   **UI Framework:** [Avalonia UI](https://avaloniaui.net/) (Cross-platform XAML)
*   **Communication:** `System.IO.Ports` for USB serial management.

---

## 🚀 Getting Started

Make sure you have the following installed on your PC:
*   [.NET SDK](https://dotnet.microsoft.com/) (version 8.0 or higher)
*   A compatible IDE like **Visual Studio 2022** or **JetBrains Rider**.

### Cloning and Running
```bash
# Clone the repository
git clone [https://github.com/MaxBoddax/LaserOne.git](https://github.com/MaxBoddax/LaserOne.git)

# Navigate to the project directory
cd LaserOne

# Build and run
dotnet run
