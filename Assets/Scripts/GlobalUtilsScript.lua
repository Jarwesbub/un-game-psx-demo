-- CAMERA POSITION CONTROL:

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
end

_G.globalSetCameraPosition = setCameraPosition -- Create public function.

-- PLAYER POSITION CONTROL:
-- Set player position based on transform values.

function setPlayerPosition(pos, rotY)
    local posX = hundredth * pos.x
    local posY = hundredth * -pos.y
    local posZ = hundredth * pos.z
    local rotYPI = DEG_TO_PI * rotY
    local player = Entity.Find("PlayerModel")

    Entity.SetPosition(player, Vec3.new(posX, posY, posZ))
    Entity.SetRotationY(player, rotYPI)
end

-- PLAYER BOUNDS CONTROL:
-- Limits the player move area.

local zero = FixedPoint.new(0)
local minX = zero
local minZ = zero
local maxX = zero
local maxZ = zero

function setPlayerBoundaries(bounds)
    minX = hundredth * bounds.min.x
    minZ = hundredth * bounds.min.z
    maxX = hundredth * bounds.max.x
    maxZ = hundredth * bounds.max.z
end

function isGlobalPlayerInBounds(pos)
    return pos.x >= minX
        and pos.x <= maxX
        and pos.z >= minZ
        and pos.z <= maxZ
end

-- Used for quick testing.
function consolePrintGlobalBoundsData()
    local pos = Player.GetPosition()
    Debug.Log("Player x: " .. pos.x .. " z: " .. pos.z)
    Debug.Log("Bounds X min: " .. minX .. ", max: " .. maxX)
    Debug.Log("Bounds Z min: " .. minZ .. ", max: " .. maxZ)
end

-- Global functions:
_G.globalSetPlayerPosition = setPlayerPosition
_G.setGlobalPlayerBoundaries = setPlayerBoundaries
_G.isGlobalPlayerInBounds = isGlobalPlayerInBounds
_G.consolePrintGlobalBoundsData = consolePrintGlobalBoundsData
