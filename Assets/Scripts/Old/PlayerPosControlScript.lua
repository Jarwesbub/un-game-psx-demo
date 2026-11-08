-- PLAYER POSITION CONTROL:

local DEG_TO_PI = FixedPoint.new(1) / 180
local hundredth = FixedPoint.new(1) / 100

function setPlayerPosition(pos, rotY)
    local posX = hundredth * pos.x
    local posY = hundredth * -pos.y
    local posZ = hundredth * pos.z
    local rotYPI = DEG_TO_PI * rotY
    local player = Entity.Find("PlayerModel")

    Entity.SetPosition(player, Vec3.new(posX, posY, posZ))
    Entity.SetRotationY(player, rotYPI)
end

_G.globalSetPlayerPosition = setPlayerPosition
