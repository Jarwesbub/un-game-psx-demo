local one = FixedPoint.new(1)

function onCreate(self)
    -- Make sure we have control of the camera
    Camera.FollowPsxPlayer(false)
    local pos = Vec3.new(12,12,-14)
    local rot = Vec3.new(44, 0, 0)

    setCameraByTransform(pos,rot)
end

-- Set camera position and rotation from the Unity Transform data.
function setCameraByTransform(pos, rot)
    local decimal = one / 100
    local posX = decimal * pos.x
    local posY = decimal * - pos.y
    local posZ = decimal * pos.z
    local rotX = decimal * - (rot.x / 2)
    local rotY = decimal * (rot.y / 2 + 10) -- Add a little bit to match with the Unity camera display.
    local rotZ = decimal * (rot.z / 2)

    Camera.SetPosition(Vec3.new(posX,posY,posZ))
    Camera.SetRotation(Vec3.new(rotX, rotY, rotZ))

    Debug.Log("Camera position set: ".. posX..", "..posY..", "..posZ)
    Debug.Log("Camera rotation set: ".. rotX..", "..rotY..", "..rotZ)
end