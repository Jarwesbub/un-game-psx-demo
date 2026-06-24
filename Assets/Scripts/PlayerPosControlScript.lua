-- Script works best with the following Camera Settings (in Unity):
-- Projection: Perspective
-- Field of View Axis: Vertical
-- Field of View: 55

-- In Game view use 4:3 aspect ratio.
local one = FixedPoint.new(1)
local DEG_TO_PI = one / 180
local hundredth = one / 100

function setPlayerPosition(pos, rotY)
    local posX = hundredth * pos.x
    local posY = hundredth * -pos.y
    local posZ = hundredth * pos.z
    local rotYPI = DEG_TO_PI * rotY
    local player = Entity.Find("PlayerModel")

    Entity.SetPosition(player, Vec3.new(posX, posY, posZ))
    Entity.SetRotationY(player, rotYPI)

    Debug.Log("setPlayerPosition var: " .. posX .. ", " .. posY .. ", " .. posZ)
end

_G.setPlayerPosition = setPlayerPosition
