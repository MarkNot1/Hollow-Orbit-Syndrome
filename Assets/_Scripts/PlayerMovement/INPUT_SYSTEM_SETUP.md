# New Input System Setup (Player)

Your project now uses **Unity's new Input System** for player input. This file explains what to assign in the Inspector and how the Input Actions asset is set up.

---

## 1. Project Settings (one-time)

1. Go to **Edit → Project Settings → Player**.
2. Under **Other Settings**, find **Active Input Handling**.
3. Set it to **Input System Package (New)** or **Both** (if you still need the old Input Manager elsewhere).
4. Restart Unity when prompted.

---

## 2. Input Actions asset used by the player

The project uses the asset:

- **`Assets/InputSystem_Actions.inputactions`**

It already contains a **Player** action map with the actions used by `PlayerInput.cs`:

| Action  | Type   | Bindings (examples)     | Used for              |
|---------|--------|-------------------------|------------------------|
| Move    | Value  | WASD, Left Stick        | Movement (Vector2→Vector3) |
| Look    | Value  | Mouse delta, Right Stick| Camera look (delta)   |
| Jump    | Button | Space, South button     | Jump                  |
| Sprint  | Button | Left Shift, Left Stick Press | Run               |
| Attack  | Button | Left mouse, West button | Mouse click (terrain) |
| Fly     | Button | V                       | Toggle fly mode       |

No need to create a new Input Actions file unless you want a separate asset for the player.

---

## 3. Inspector setup (required)

### GameObject with `PlayerInput`

1. Select the **player GameObject** that has the **PlayerInput** component (usually the root player or the same object as **Character**).
2. In the Inspector, find the **PlayerInput** component.
3. Assign the **Input Actions** field:
   - Drag **`InputSystem_Actions`** (the `.inputactions` asset) into the **Input Actions** slot,  
   **or**
   - Click the circle and choose **InputSystem_Actions** from the list.

If this field is empty, you will get errors in the console and no input will work.

### Optional: `PlayerCamera`

- **Player Input**: Usually left empty; it is set in code from `GetComponentInParent<PlayerInput>()` if the camera is a child of the player.
- **Player Body**: Assign the **Transform** that should rotate for horizontal look (e.g. the parent body or capsule).
- **Sensitivity**: Default `0.15` (degrees per pixel). Tweak to taste; with the new Input System, Look uses **pointer delta** (pixels), not the old axis values.

### Optional: `Character`

- **Player Input**: Can be left empty; it is set in code from `GetComponent<PlayerInput>()` on the same GameObject.

---

## 4. How to open / edit the Input Actions asset

1. In the **Project** window, go to **Assets** and select **InputSystem_Actions**.
2. In the Inspector, click **Open** (or double-click the asset) to open the **Input Actions** editor.
3. Select the **Player** map to see or edit:
   - **Actions**: Add/remove actions, change type (Value/Button/Pass Through).
   - **Bindings**: Add/remove bindings (keys, mouse, gamepad).

After editing, save the asset (Ctrl+S). No code changes are needed unless you add or rename actions; in that case update `PlayerInput.cs` to use the new action names.

---

## 5. Creating a new Input Actions asset (optional)

If you prefer a **new** asset only for the player (instead of using `InputSystem_Actions`):

1. In the Project window: **Right-click → Create → Input Actions** (or **Assets → Create → Input Actions**).
2. Name it (e.g. `PlayerInputActions`).
3. Open the asset and add an **Action Map** named **Player**.
4. Add these **Actions** in that map:

   - **Move** – Type: **Value**, Control Type: **Vector 2**.  
     Add a **2D Vector** composite: W/S (up/down), A/D (left/right), or use **WASD** composite.
   - **Look** – Type: **Value**, Control Type: **Vector 2**.  
     Add binding **&lt;Pointer&gt;/delta** (and optionally **&lt;Gamepad&gt;/rightStick**).
   - **Jump** – Type: **Button**.  
     Bind **&lt;Keyboard&gt;/space** (and optionally gamepad South).
   - **Sprint** – Type: **Button**.  
     Bind **&lt;Keyboard&gt;/leftShift**.
   - **Attack** – Type: **Button**.  
     Bind **&lt;Mouse&gt;/leftButton**.
   - **Fly** – Type: **Button**.  
     Bind **&lt;Keyboard&gt;/v**.

5. Save the asset.
6. In the **PlayerInput** component, assign this new asset to **Input Actions** instead of `InputSystem_Actions`.

---

## 6. Summary checklist

- [ ] **Project Settings**: Active Input Handling = **Input System Package (New)** (or **Both**).
- [ ] **PlayerInput** component: **Input Actions** = **InputSystem_Actions** (or your custom Input Actions with a **Player** map and the actions above).
- [ ] **PlayerCamera**: **Player Body** = Transform that rotates for horizontal look; **Sensitivity** tuned if needed.
- [ ] Player hierarchy: Camera is under the same GameObject that has **PlayerInput** (or **PlayerInput** is on the same object as **Character**), so `GetComponentInParent<PlayerInput>()` and `GetComponent<PlayerInput>()` find it.

After this, movement, look, jump, sprint, mouse click, and fly (V) all run through the new Input System.
