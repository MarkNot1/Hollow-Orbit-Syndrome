using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Circuit_Utilities : MonoBehaviour
{
    public static Vector3 SnapToGrid(Vector3 position)
    {
        // Snaps the position to the nearest grid point
        float x = Mathf.Round(position.x / Circuit_ConnectionManager.Instance.BOARD_GRID_SIZE) * Circuit_ConnectionManager.Instance.BOARD_GRID_SIZE;
        float y = Mathf.Round(position.y / Circuit_ConnectionManager.Instance.BOARD_GRID_SIZE) * Circuit_ConnectionManager.Instance.BOARD_GRID_SIZE;
        return new Vector3(x, y, position.z);
    }
    public static Vector2 SnapToGrid2D(Vector2 position)
    {
        // Snaps the position to the nearest grid point
        float x = Mathf.Round(position.x / Circuit_ConnectionManager.Instance.BOARD_GRID_SIZE_2D) * Circuit_ConnectionManager.Instance.BOARD_GRID_SIZE_2D;
        float y = Mathf.Round(position.y / Circuit_ConnectionManager.Instance.BOARD_GRID_SIZE_2D) * Circuit_ConnectionManager.Instance.BOARD_GRID_SIZE_2D;
        return new Vector2(x , y)+ Circuit_ConnectionManager.Instance.BOARD_GRID_SIZE_2D_OFFSET;
    }
    public static Vector3 GetMouseWorldPosition()
    {
        // Convert mouse position to world space
        Vector3 mousePosition = Input.mousePosition;
        mousePosition.z = 100f; // Distance from the camera to the canvas
        return Camera.main.ScreenToWorldPoint(mousePosition);
    }
    public static bool IsPointerOverCircuitBoard(RectTransform CircuitBoard, Canvas MainCanvas)
    {
        // Convert mouse position to screen point
        Vector2 mousePosition = Input.mousePosition;

        // If the canvas is set to Screen Space - Camera, we need to pass the camera to the function
        return RectTransformUtility.RectangleContainsScreenPoint(CircuitBoard, mousePosition, MainCanvas.renderMode == RenderMode.ScreenSpaceCamera ? MainCanvas.worldCamera : null);
    }
    public static string FormatCurrent(double amps)
    {
        // Define suffixes for different ranges
        string[] suffixes = { "A", "mA", "µA" };

        int index = 0;

        // Convert amps to milliamps or microamps
        while (amps < 1 && index < suffixes.Length - 1)
        {
            amps *= 1000;
            index++;
        }

        // Return the formatted string
        return $"{amps:0.#}{suffixes[index]}";
    }
    public static string FormatVoltage(double volts)
    {
        // Define suffixes for different ranges
        string[] suffixes = { "V", "mV", "µV" };

        int index = 0;

        // Convert amps to milliamps or microamps
        while (volts < 1 && index < suffixes.Length - 1)
        {
            volts *= 1000;
            index++;
        }

        // Return the formatted string
        return $"{volts:0.#}{suffixes[index]}";
    }
    public static string FormatOhms(double value)
    {
        // Define suffixes for different ranges
        string[] suffixes = { " Ohm", " kOhm", " MOhm", " GOhm" };

        int index = 0;

        // Divide the number until it's smaller than 1000
        while (value >= 1000 && index < suffixes.Length - 1)
        {
            value /= 1000;
            index++;
        }

        // Return the formatted string
        return $"{value:0.#}{suffixes[index]}";
    }
    public static string FormatFarads(double value)
    {
        // Define suffixes for different ranges
        string[] suffixes = { "F", "mF", "µF" };

        int index = 0;

        // Convert amps to milliamps or microamps
        while (value < 1 && index < suffixes.Length - 1)
        {
            value *= 1000;
            index++;
        }

        // Return the formatted string
        return $"{value:0.#}{suffixes[index]}";
    }
}
