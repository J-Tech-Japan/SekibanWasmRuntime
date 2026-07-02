import Foundation
import XCTest
@testable import SekibanMv
@testable import SekibanWasm

final class PackPtrLenTests: XCTestCase {
    func testRoundTripPositiveValues() {
        let packed = packPtrLen(0x1234_5678, 42)
        let (ptr, len) = unpackPtrLen(packed)
        XCTAssertEqual(ptr, 0x1234_5678)
        XCTAssertEqual(len, 42)
    }

    func testRoundTripHighBitAddresses() {
        // Linear-memory addresses above 2 GiB arrive as negative Int32 bit patterns.
        let ptr = Int32(bitPattern: 0x8000_0001)
        let len = Int32(bitPattern: 0xFFFF_FFFF)
        let (outPtr, outLen) = unpackPtrLen(packPtrLen(ptr, len))
        XCTAssertEqual(outPtr, ptr)
        XCTAssertEqual(outLen, len)
    }

    func testZeroPacksToZero() {
        XCTAssertEqual(packPtrLen(0, 0), 0)
    }
}

final class MvParamBuilderTests: XCTestCase {
    func testBuildsParamsWithMatchingKinds() {
        let id = UUID()
        let params = MvParamBuilder()
            .guid("Id", id)
            .string("Name", "meeting-room-1")
            .int32("Capacity", 12)
            .bool("Active", true)
            .null("Comment")
            .build()

        XCTAssertEqual(params.map(\.name), ["Id", "Name", "Capacity", "Active", "Comment"])
        XCTAssertEqual(
            params.map(\.kind),
            [.guid, .string, .int32, .boolean, .null])
        XCTAssertNil(params[4].valueJson)
    }

    func testParamKindsEncodeAsHostContractIntegers() throws {
        // The host decodes MvParamKind as the raw 0..9 integer; a change here is a
        // wire-contract break.
        let param = MvParam(name: "K", kind: .guid, valueJson: "\"00000000-0000-0000-0000-000000000000\"")
        let json = String(data: try JSONEncoder().encode(param), encoding: .utf8)!
        XCTAssertTrue(json.contains("\"kind\":5"), "unexpected encoding: \(json)")
    }
}
