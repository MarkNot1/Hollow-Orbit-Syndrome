using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public class PlayerCameraTests
{
    private GameObject body;
    private PlayerCamera look;
    private PlayerInput input;
    private CursorLockMode cursorLock;
    private bool cursorVisible;

    [SetUp]
    public void SetUp()
    {
        cursorLock = Cursor.lockState;
        cursorVisible = Cursor.visible;
        body = new GameObject("Camera test player");
        body.SetActive(false);
        input = body.AddComponent<PlayerInput>();
        GameObject target = new GameObject("Look target");
        target.transform.SetParent(body.transform, false);
        look = target.AddComponent<PlayerCamera>();
        Set("playerBody", body.transform);
        Call("Awake");
        Call("OnEnable");
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(body);
        Cursor.lockState = cursorLock;
        Cursor.visible = cursorVisible;
    }

    [TestCase(30)]
    [TestCase(60)]
    [TestCase(144)]
    public void MouseTravelConvergesToTheSameAngleAtDifferentFrameRates(int fps)
    {
        for (int i = 0; i < fps; i++) Step(new Vector2(100f / fps, 0f), 1f / fps);
        for (int i = 0; i < fps; i++) Step(Vector2.zero, 1f / fps);
        Assert.That(Mathf.DeltaAngle(12f, look.transform.eulerAngles.y), Is.EqualTo(0f).Within(0.001f));
    }

    [Test]
    public void RenderLookUpdatesBetweenPhysicsTicksAndIgnoresInterpolatedBodyYaw()
    {
        Step(new Vector2(100f, 0f), 1f / 144f);
        Assert.That(look.transform.eulerAngles.y, Is.InRange(0.01f, 11.99f));
        Assert.That(body.transform.eulerAngles.y, Is.Zero);
        for (int i = 0; i < 144; i++) Step(Vector2.zero, 1f / 144f);
        body.transform.rotation = Quaternion.Euler(0f, 7f, 0f);
        Step(Vector2.zero, 1f / 144f);
        Assert.That(Mathf.DeltaAngle(12f, look.transform.eulerAngles.y), Is.EqualTo(0f).Within(0.001f));
    }

    [Test]
    public void LookWrapsYawAndClampsPitchWithoutOvershoot()
    {
        look.transform.rotation = Quaternion.Euler(0f, 350f, 0f);
        Call("ResetLook");
        Step(new Vector2(200f, 10000f), 1f / 60f);
        for (int i = 0; i < 120; i++) Step(Vector2.zero, 1f / 60f);
        Assert.That(Quaternion.Angle(look.transform.rotation, Quaternion.Euler(-90f, 14f, 0f)), Is.LessThan(0.01f));
    }

    [Test]
    public void BlockingInputDiscardsSmoothingTailAndStopsBodyRotation()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Step(new Vector2(100f, 0f), 1f / 60f);
        Quaternion before = look.transform.rotation;
        input.SetGameplayInputBlocked(true);
        Call("LateUpdate");
        Call("FixedUpdate");
        Assert.That(body.transform.rotation, Is.EqualTo(Quaternion.identity));
        input.SetGameplayInputBlocked(false);
        for (int i = 0; i < 60; i++) Step(Vector2.zero, 1f / 60f);
        Assert.That(Quaternion.Angle(before, look.transform.rotation), Is.LessThan(0.01f));
    }

    [Test]
    public void PrefabUsesReducedSensitivityAndInterpolatedMovement()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Prefabs/Player.prefab");
        var serialized = new SerializedObject(prefab.GetComponentInChildren<PlayerCamera>(true));
        Assert.That(serialized.FindProperty("sensitivity").floatValue, Is.EqualTo(0.12f));
        Assert.That(serialized.FindProperty("lookSmoothTime").floatValue, Is.EqualTo(0.04f));
        Assert.That(prefab.GetComponent<Rigidbody>().interpolation, Is.EqualTo(RigidbodyInterpolation.Interpolate));
    }

    private void Step(Vector2 delta, float time) => Call("UpdateLook", delta, time);
    private void Call(string method, params object[] arguments) =>
        typeof(PlayerCamera).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(look, arguments);
    private void Set(string field, object value) =>
        typeof(PlayerCamera).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(look, value);
}
