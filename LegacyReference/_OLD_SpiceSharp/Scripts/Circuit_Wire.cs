
using UnityEngine;
using UnityEngine.UI;

public class Circuit_Wire : Circuit_Component
{
    public LineRenderer lineRenderer;
    public string wireName;
    public Text InfoText;
    public Circuit_ComponentNode firstNode;
    public Circuit_ComponentNode SecondNode;
    // Start is called before the first frame update
    void Start()
    {
        Circuit_ConnectionManager.Instance.AddWire(this);
        LineSelector.Instance.AddLineRenderer(lineRenderer);
    }
    private void Update()
    {
        InfoText.transform.position = (lineRenderer.GetPosition(0) + lineRenderer.GetPosition(1)) / 2f;
        if (Nodes.Count > 0 && Nodes[0] != null)
        {
            lineRenderer.SetPosition(0, Nodes[0].transform.position);
        }
        if (Nodes.Count > 1 && Nodes[1] != null)
        {
            lineRenderer.SetPosition(1, Nodes[1].transform.position);
        }
    }
    public void SetParameters(float volts, float Amps)
    {
       // Debug.Log(volts.ToString("0.00") + "V , " + Amps + "A");
        InfoText.text = Circuit_Utilities.FormatVoltage(volts) + "\n" + Circuit_Utilities.FormatCurrent(Amps);
        //lineRenderer.GetComponent<ElectronSimulator>().voltage = volts;
       
    }

    public void CorrectDirection()
    {

    }
    public void DetatchNodes()
    {
        Destroy(this.gameObject);
      
    }
}
