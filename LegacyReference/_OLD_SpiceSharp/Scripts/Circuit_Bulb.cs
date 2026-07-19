using System;
using System.Collections.Generic;
using System.Linq;
using SpiceSharp.Simulations;
using UnityEngine;
using UnityEngine.UI;


public class Circuit_Bulb :Circuit_Component
{
    public Image BulbLight;
    public float BurnVoltage =14;
    public float BurnCurrent;
    public float watts;
    [HideInInspector]
    public float resistance;
    public bool IsBurnt;
    public Color lightColor;
    public float BulbOnVoltage;
    public float BulbSafeVoltage;

    private void Start()
    {
        Circuit_Component_Item component = new Circuit_Component_Item();
        //Circuit_Bulb newBulb = Instantiate(BulbPrefab, ComponentParent);
        //newBulb.transform.position = ComponentSpawnPosition.position;
        component.component = this;
        component.componentType = CIRCUIT_COMPONENT_TYPE.BULB;
        int bulbCount = Circuit_ConnectionManager.Instance.ComponentsList.Where(x => x.componentType == CIRCUIT_COMPONENT_TYPE.BULB).Count();
        component.component.gameObject.name = "Bulb" + bulbCount.ToString();
        component.component.Name = component.component.gameObject.name;
        Circuit_ConnectionManager.Instance.ComponentsList.Add(component);
    }
    private void Update()
    {
        resistance = (BurnVoltage * BurnVoltage) / watts;
        Label.text = BurnVoltage + "V " + watts + "W";
    }
   
    public void GetVoltageAcrossBulb(DC dc)
    {
       double voltage =  SimulationHelper.GetVoltage(dc, Nodes[0].NodeName, Nodes[1].NodeName);
        
        if (IsBurnt)
        {
            BulbLight.color = Color.red;
            return;
        }
        BulbLight.color = HexToColor(GetColorForValue(voltage));
        if (voltage >= BurnVoltage)
        {
            IsBurnt = true;
        }
        
    }
    // Function to interpolate between two colors
    public (int r, int g, int b) InterpolateColor((int r, int g, int b) color1, (int r, int g, int b) color2, double factor)
    {
        return (
            (int)(color1.r + (color2.r - color1.r) * factor),
            (int)(color1.g + (color2.g - color1.g) * factor),
            (int)(color1.b + (color2.b - color1.b) * factor)
        );
    }

    // Function to convert RGB to Hex
    public string RGBToHex((int r, int g, int b) rgb)
    {
        return $"#{rgb.r:X2}{rgb.g:X2}{rgb.b:X2}";
    }
    public string GetColorForValue(double value)
    {
        // Define the color stops in RGB
        var white = (255, 255, 255);  // White
        var yellow = (255, 255, 0);   // Yellow
        var red = (255, 0, 0);        // Red

        if (value <= BulbOnVoltage)
        {
            return RGBToHex(white); // Below or equal to 9, it's white
        }
        else if (value <= BulbSafeVoltage)
        {
            // Interpolate between white and yellow (9 to 11 range)
            double factor = (value - BulbOnVoltage) / (BulbSafeVoltage - BulbOnVoltage); // Scale to [0, 1]
            var color = InterpolateColor(white, yellow, factor);
            return RGBToHex(color);
        }
        else if (value <= (int)BurnVoltage)
        {
            // Interpolate between yellow and red (11 to 14 range)
            double factor = (value - BulbSafeVoltage) / (BurnVoltage - BulbSafeVoltage); // Scale to [0, 1]
            var color = InterpolateColor(yellow, red, factor);
            return RGBToHex(color);
        }
        else
        {
            return RGBToHex(red); // Above 14, it's red
        }
    }
    // Function to convert a hex string to a Unity Color
    public static Color HexToColor(string hex)
    {
        // Remove the '#' character if it's present
        if (hex.StartsWith("#"))
        {
            hex = hex.Substring(1);
        }

        // Parse the hex value
        if (hex.Length == 6) // RGB format
        {
            byte r = (byte)(Convert.ToUInt32(hex.Substring(0, 2), 16));
            byte g = (byte)(Convert.ToUInt32(hex.Substring(2, 2), 16));
            byte b = (byte)(Convert.ToUInt32(hex.Substring(4, 2), 16));
            return new Color(r / 255f, g / 255f, b / 255f);
        }
        else if (hex.Length == 8) // RGBA format
        {
            byte r = (byte)(Convert.ToUInt32(hex.Substring(0, 2), 16));
            byte g = (byte)(Convert.ToUInt32(hex.Substring(2, 2), 16));
            byte b = (byte)(Convert.ToUInt32(hex.Substring(4, 2), 16));
            byte a = (byte)(Convert.ToUInt32(hex.Substring(6, 2), 16));
            return new Color(r / 255f, g / 255f, b / 255f, a / 255f);
        }
        else
        {
            Debug.LogError("Invalid hex color format. Use #RRGGBB or #RRGGBBAA.");
            return Color.white; // Default color on error
        }
    }

}
