local pointA = Entity.Find("PointA")
local pointB = Entity.Find("PointB")

function onCreate(self)
    Camera.FollowPsxPlayer(false)
    pointA = Entity.Find("PointA")
    pointB = Entity.Find("PointB")
    Debug.Log("Scene1Control onCreate()")
    local fow = 280 -- Unity FOV: 55
    Camera.SetH(fow)
    setCameraPositionById(0)
end

function setCameraPositionById(number)
    if number == 0 then
        local pos = Vec3.new(-20, 7, 0)
        local rot = Vec3.new(8, 90, 0)

        setPlayerBoundaries(-9, -9, 9, 9)
        setCameraPosition(pos, rot)
    elseif number == 1 then
        local pos = Vec3.new(-47, 12, -20)
        local rot = Vec3.new(20, 53, 0)

        setPlayerBoundaries(-70, -14, -6, 14)
        setCameraPosition(pos, rot)
    elseif number == 2 then
        local pos = Vec3.new(-31, 11, -20)
        local rot = Vec3.new(26, -28, 0)
        setCameraPosition(pos, rot)
    elseif number == 3 then
        local pos = Vec3.new(-35, 13, 0)
        local rot = Vec3.new(20, -90, 0)
        setCameraPosition(pos, rot)
    end
end

_G.setCameraPositionById = setCameraPositionById


local hundredth = FixedPoint.new(1) / 100
function setPlayerBoundaries(minX, minZ, maxX, maxZ)
    local minPosX = hundredth * minX
    local minPosZ = hundredth * minZ
    local maxPosX = hundredth * maxX
    local maxPosZ = hundredth * maxZ

    Entity.SetPosition(pointA, Vec3.new(minPosX, 0, minPosZ))
    Entity.SetPosition(pointB, Vec3.new(maxPosX, 0, maxPosZ))

    Debug.Log("PointA: " .. pointA.position.x .. ", " .. pointA.position.z)
end
