local hundredth = FixedPoint.new(1) / 100
local zero = FixedPoint.new(0)
local minX = zero
local minZ = zero
local maxX = zero
local maxZ = zero

function setGlobalPlayerBoundaries(bounds)
    minX = hundredth * bounds.min.x
    minZ = hundredth * bounds.min.z
    maxX = hundredth * bounds.max.x
    maxZ = hundredth * bounds.max.z
end

_G.setGlobalPlayerBoundaries = setGlobalPlayerBoundaries

function isGlobalPlayerInBounds(pos)
    return pos.x >= minX
        and pos.x <= maxX
        and pos.z >= minZ
        and pos.z <= maxZ
end

_G.isGlobalPlayerInBounds = isGlobalPlayerInBounds

-- Used for quick testing.
function consolePrintGlobalBoundsData()
    local pos = Player.GetPosition()
    Debug.Log("Player x: " .. pos.x .. " z: " .. pos.z)
    Debug.Log("Bounds X min: " .. minX .. ", max: " .. maxX)
    Debug.Log("Bounds Z min: " .. minZ .. ", max: " .. maxZ)
end

_G.consolePrintGlobalBoundsData = consolePrintGlobalBoundsData
