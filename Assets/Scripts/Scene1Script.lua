local currentCameraId = 0

function setCameraToNextPosition()
    currentCameraId = currentCameraId + 1
    setCameraPositionById(currentCameraId)
end

--_G.setCameraToNextPosition = setCameraToNextPosition

function setCameraToPrevPosition()
    if currentCameraId > 0 then
        currentCameraId = currentCameraId - 1
        setCameraPositionById(currentCameraId)
    end
end

--_G.setCameraToPrevPosition = setCameraToPrevPosition

function setCameraPositionById(number)
    if number == 0 then
        local pos = Vec3.new(5, 4, -20)
        local rot = Vec3.new(0, 0, 0)
        Debug.Log("Camera position set to 0: " .. pos.x .. ", " .. pos.z)
        setCameraPosition(pos, rot)
    elseif number == 1 then
        local pos = Vec3.new(0, 4, -20)
        local rot = Vec3.new(0, 0, 0)
        Debug.Log("Camera position set to 1: " .. pos.x .. ", " .. pos.z)
        setCameraPosition(pos, rot)
    elseif number == 2 then
        local pos = Vec3.new(-5, 4, -20)
        local rot = Vec3.new(0, 0, 0)
        Debug.Log("Camera position set to 1: " .. pos.x .. ", " .. pos.z)
        setCameraPosition(pos, rot)
    end
end

_G.setCameraPositionById = setCameraPositionById
