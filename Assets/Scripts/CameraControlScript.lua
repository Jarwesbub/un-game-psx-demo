-- Script works best with the following Camera Settings (in Unity):
-- Projection: Perspective
-- Field of View Axis: Vertical
-- Field of View: 55

-- In Game view use 4:3 aspect ratio.
local DEG_TO_PI = FixedPoint.new(1) / 180
local hundredth = FixedPoint.new(1) / 100

-- Set camera position and rotation from the Unity Transform data.
function setCameraPosition(pos, rot)
    local posX = hundredth * pos.x
    local posY = hundredth * -pos.y
    local posZ = hundredth * pos.z
    local rotX = -rot.x * DEG_TO_PI
    local rotY = rot.y * DEG_TO_PI
    local rotZ = rot.z * DEG_TO_PI

    Camera.SetPosition(Vec3.new(posX, posY, posZ))
    Camera.SetRotation(Vec3.new(rotX, rotY, rotZ))

    Debug.Log("Camera rot: " .. -rot.x .. ", " .. rot.y .. ", " .. rot.z)
    Debug.Log("Camera pi-units: " .. rotX .. ", " .. rotY .. ", " .. rotZ)
end

_G.setCameraPosition = setCameraPosition -- Create public function.
