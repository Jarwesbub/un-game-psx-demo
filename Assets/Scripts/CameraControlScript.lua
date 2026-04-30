-- Script works best with the following Camera Settings (in Unity):
-- Projection: Perspective
-- Field of View Axis: Vertical
-- Field of View: 55

-- In Game view use 4:3 aspect ratio.
-- NOTE: Rotation values are not accurate due the FOV.

local one = FixedPoint.new(1)
local hundredth = FixedPoint.new(1) / 100

local maxPosA = Entity.Find("PointA") -- Player's max position
local minPosB = Entity.Find("PointB") -- Player's min position

function onCreate(self)
    maxPosA = Entity.Find("PointA")
    minPosB = Entity.Find("PointB")

    Camera.FollowPsxPlayer(false)
    local pos = Vec3.new(0, 4, -20)
    local rot = Vec3.new(0, 0, 0)

    --local fow = Camera.GetH()
    local fow = 280 -- Unity FOV: 55
    Camera.SetH(fow)
    Debug.Log("Camera FOV: " .. fow)
    setCameraByTransform(pos, rot)
end

-- Set camera position and rotation from the Unity Transform data.
function setCameraByTransform(pos, rot)
    local decimal = one / 100
    local rotDiff = rot.x / 2 -- 1.96
    local posX = decimal * pos.x
    local posY = decimal * -pos.y
    local posZ = decimal * pos.z
    local rotX = -decimal * rot.x
    local rotY = decimal * rot.y
    local rotZ = decimal * rot.z

    Camera.SetPosition(Vec3.new(posX, posY, posZ))
    Camera.SetRotation(Vec3.new(rotX, rotY, rotZ))

    Debug.Log("Camera position set: " .. posX .. ", " .. posY .. ", " .. posZ)
    Debug.Log("Camera rotation set: " .. rotX .. ", " .. rotY .. ", " .. rotZ)
end
