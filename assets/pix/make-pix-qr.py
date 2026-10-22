"""Builds the static Pix "copia e cola" code (BR Code, EMV) for the Support window and renders it as a QR PNG.

Run once when the key changes (needs `pip install segno zxing-cpp pillow`):
    python assets/pix/make-pix-qr.py
It writes assets/pix/pix-qr.png and prints the code, then decodes the PNG again to check it.
"""
import pathlib
import segno

KEY = "f1f20b16-c964-4465-9f45-2cb2a7647726"
NAME = "VEMRYX"   # shown by some banks; the bank always shows the real account holder too
CITY = "BRASIL"   # required by the format, not checked against the account


def field(tag: str, value: str) -> str:
    return f"{tag}{len(value):02d}{value}"


def crc16(data: str) -> str:
    """CRC-16/CCITT-FALSE (poly 0x1021, init 0xFFFF), as the BR Code spec requires."""
    crc = 0xFFFF
    for byte in data.encode("utf-8"):
        crc ^= byte << 8
        for _ in range(8):
            crc = ((crc << 1) ^ 0x1021) if crc & 0x8000 else (crc << 1)
            crc &= 0xFFFF
    return f"{crc:04X}"


def payload() -> str:
    account = field("00", "br.gov.bcb.pix") + field("01", KEY)
    body = (field("00", "01") + field("26", account) + field("52", "0000") + field("53", "986") +
            field("58", "BR") + field("59", NAME) + field("60", CITY) + field("62", field("05", "***")) + "6304")
    return body + crc16(body)


if __name__ == "__main__":
    code = payload()
    out = pathlib.Path(__file__).with_name("pix-qr.png")
    segno.make(code, error="m", micro=False).save(out, scale=8, border=2, dark="#000000", light="#ffffff")
    print(code)
    import zxingcpp
    from PIL import Image
    read = zxingcpp.read_barcodes(Image.open(out))
    assert read and read[0].text == code, "the PNG does not decode to the code"
    print("ok:", out)
